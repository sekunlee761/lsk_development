using System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Core
{
    // Simple folder-based recipe repository with metadata index (index.json).
    // This is an example implementation to demonstrate storage and indexing.
    public class RecipeRepository
    {
        private readonly string _folder;
        private readonly string _indexPath;

        public RecipeRepository(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentNullException(nameof(folder));
            _folder = folder;
            _indexPath = Path.Combine(_folder, "index.json");
            EnsureFolder();
        }

        private void EnsureFolder()
        {
            if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);
        }

        // Metadata for listing and quick lookup.
        public class Metadata
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
            public DateTime Timestamp { get; set; }
            public string FileName { get; set; }
        }

        // Save or update recipe. Returns true on success.
        public bool SaveRecipe(RecipeBase recipe, out string message)
        {
            message = null;
            if (recipe == null) { message = "레시피가 null 입니다."; return false; }
            if (string.IsNullOrWhiteSpace(recipe.Id)) { message = "레시피 Id가 없습니다."; return false; }

            try
            {
                EnsureFolder();
                var fileName = recipe.Id + ".json";
                var path = Path.Combine(_folder, fileName);

                // Try JSON (Newtonsoft) first; if not available, fallback to XML
                try
                {
                    var json = RecipeSerializer.ToJson(recipe);
                    File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                    fileName = recipe.Id + ".json";
                    path = Path.Combine(_folder, fileName);
                }
                catch
                {
                    // Fallback to XML
                    var xml = RecipeSerializer.ToXml(recipe);
                    fileName = recipe.Id + ".xml";
                    path = Path.Combine(_folder, fileName);
                    File.WriteAllText(path, xml, System.Text.Encoding.UTF8);
                }

                // Update index
                var index = LoadIndex();
                var meta = index.FirstOrDefault(m => string.Equals(m.Id, recipe.Id, StringComparison.OrdinalIgnoreCase));
                if (meta == null)
                {
                    meta = new Metadata();
                    index.Add(meta);
                }

                meta.Id = recipe.Id;
                meta.Name = recipe.Name;
                meta.Version = recipe.Version;
                meta.Timestamp = recipe.Timestamp;
                meta.FileName = fileName;

                SaveIndex(index);
                message = "저장 완료";
                return true;
            }
            catch (Exception ex)
            {
                message = "저장 실패: " + ex.Message;
                return false;
            }
        }

        // Load recipe by id. Generic type T allows concrete recipe type to be deserialized.
        public T LoadRecipe<T>(string id) where T : RecipeBase
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var jsonPath = Path.Combine(_folder, id + ".json");
            var xmlPath = Path.Combine(_folder, id + ".xml");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                    return RecipeSerializer.FromJson<T>(json);
                }
                catch
                {
                    // fall through to try xml
                }
            }

            if (File.Exists(xmlPath))
            {
                try
                {
                    var xml = File.ReadAllText(xmlPath, System.Text.Encoding.UTF8);
                    return RecipeSerializer.FromXml<T>(xml);
                }
                catch { }
            }

            return null;
        }

        public bool DeleteRecipe(string id, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(id)) { message = "유효하지 않은 Id"; return false; }

            try
            {
                var path = Path.Combine(_folder, id + ".json");
                if (File.Exists(path)) File.Delete(path);

                var index = LoadIndex();
                var meta = index.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                if (meta != null) { index.Remove(meta); SaveIndex(index); }

                message = "삭제 완료";
                return true;
            }
            catch (Exception ex)
            {
                message = "삭제 실패: " + ex.Message;
                return false;
            }
        }

        public IReadOnlyCollection<Metadata> ListMetadata()
        {
            var index = LoadIndex();
            return index.AsReadOnly();
        }

        private List<Metadata> LoadIndex()
        {
            try
            {
                if (!File.Exists(_indexPath)) return new List<Metadata>();
                // Prefer JSON index if file contains JSON; try to detect by content
                var content = File.ReadAllText(_indexPath, System.Text.Encoding.UTF8);
                try
                {
                    // Try JSON via Newtonsoft if available
                    var list = RecipeSerializer.TryDeserializeListWithNewtonsoft<Metadata>(content);
                    if (list != null) return list;
                }
                catch { }

                // Fallback to XML
                try
                {
                    var list = RecipeSerializer.FromXml<List<Metadata>>(content);
                    return list ?? new List<Metadata>();
                }
                catch
                {
                    return new List<Metadata>();
                }
            }
            catch
            {
                return new List<Metadata>();
            }
        }

        private void SaveIndex(List<Metadata> index)
        {
            try
            {
                try
                {
                    // Try JSON via Newtonsoft
                    var json = RecipeSerializer.TrySerializeWithNewtonsoftObject(index);
                    if (json != null)
                    {
                        File.WriteAllText(_indexPath, json, System.Text.Encoding.UTF8);
                        return;
                    }
                }
                catch { }

                // Fallback to XML
                try
                {
                    var xml = RecipeSerializer.ToXml(index);
                    File.WriteAllText(_indexPath, xml, System.Text.Encoding.UTF8);
                }
                catch { }
            }
            catch
            {
                // swallow - index save failure should not throw to caller in this simple example
            }
        }
    }
}

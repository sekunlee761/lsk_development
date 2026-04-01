using System;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core
{
    // Thread-safe singleton manager for recipes
    public class RecipeManager : CSingleton<RecipeManager>
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, RecipeBase> _recipes = new Dictionary<string, RecipeBase>(StringComparer.OrdinalIgnoreCase);

        // Currently active recipe
        private RecipeBase _activeRecipe = null;
        // Last active recipe before the current one
        private RecipeBase _lastRecipe = null;

        // 이벤트: 활성 레시피 변경 시 발행 (OldRecipe -> NewRecipe). NewRecipe가 null이면 비활성화 의미
        public event EventHandler<RecipeChangedEventArgs> ActiveRecipeChanged;

        public override void Initialize()
        {
            // nothing by default
        }

        public override void Shutdown()
        {
            lock (_lock)
            {
                _recipes.Clear();
                _activeRecipe = null;
                _lastRecipe = null;
            }
        }

        // Register a recipe instance. If a recipe with same Id exists, it will be replaced.
        public void RegisterRecipe(RecipeBase recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));

            lock (_lock)
            {
                _recipes[recipe.Id] = recipe;
            }
        }

        // Create a new recipe instance of type T, register it and return the instance.
        public T CreateRecipe<T>(string name = null) where T : RecipeBase, new()
        {
            var r = new T();
            r.Id = Guid.NewGuid().ToString();
            if (!string.IsNullOrWhiteSpace(name)) r.Name = name;
            r.Timestamp = DateTime.UtcNow;
            RegisterRecipe(r);
            return r;
        }

        // Copy an existing recipe (deep clone) and register the new recipe. Returns new Id on success.
        public bool CopyRecipe(string sourceId, out string newId, out string message)
        {
            newId = null;
            message = null;
            if (string.IsNullOrWhiteSpace(sourceId)) { message = "유효하지 않은 레시피 ID입니다."; return false; }

            RecipeBase source = null;
            lock (_lock)
            {
                _recipes.TryGetValue(sourceId, out source);
            }

            if (source == null) { message = "원본 레시피를 찾을 수 없습니다."; return false; }

            // Deep clone using serializer
            try
            {
                var type = source.GetType();
                var cloneMethod = typeof(RecipeSerializer).GetMethod("Clone").MakeGenericMethod(type);
                var clonedObj = cloneMethod.Invoke(null, new object[] { source });
                var cloned = clonedObj as RecipeBase;

                if (cloned == null) { message = "복제에 실패했습니다."; return false; }

                cloned.Id = Guid.NewGuid().ToString();
                cloned.Name = cloned.Name + " (복사)";
                cloned.Timestamp = DateTime.UtcNow;

                RegisterRecipe(cloned);
                newId = cloned.Id;
                message = "성공";
                return true;
            }
            catch (Exception ex)
            {
                message = "복제 실패: " + ex.Message;
                return false;
            }
        }

        // Unregister recipe by id
        public bool UnregisterRecipe(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (_lock)
            {
                return _recipes.Remove(id);
            }
        }

        // Update an existing recipe (registers/overwrites). Returns success and message.
        public bool UpdateRecipe(RecipeBase recipe, out string message)
        {
            if (recipe == null) { message = "레시피가 null 입니다."; return false; }
            if (string.IsNullOrWhiteSpace(recipe.Id)) { message = "레시피 Id가 없습니다."; return false; }

            if (!recipe.Validate(out var vmsg)) { message = "레시피 유효성 검사 실패: " + vmsg; return false; }

            lock (_lock)
            {
                // If updating active recipe, update active reference too
                var isActive = _activeRecipe != null && string.Equals(_activeRecipe.Id, recipe.Id, StringComparison.OrdinalIgnoreCase);
                _recipes[recipe.Id] = recipe;
                if (isActive)
                {
                    _activeRecipe = recipe;
                }
            }

            message = "성공";
            return true;
        }

        // Get a recipe by id
        public RecipeBase GetRecipe(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_lock)
            {
                _recipes.TryGetValue(id, out var r);
                return r;
            }
        }

        // List all recipe ids
        public IReadOnlyCollection<RecipeBase> GetAllRecipes()
        {
            lock (_lock)
            {
                return new List<RecipeBase>(_recipes.Values).AsReadOnly();
            }
        }

        // Activate a recipe by id. Returns true if activation succeeded.
        public bool ActivateRecipe(string id, out string message)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                message = "유효하지 않은 레시피 ID입니다.";
                return false;
            }

            RecipeBase oldRecipe = null;
            RecipeBase newRecipe = null;
            lock (_lock)
            {
                if (!_recipes.TryGetValue(id, out var recipe))
                {
                    message = "레시피를 찾을 수 없습니다.";
                    return false;
                }

                // Validate before activation
                if (!recipe.Validate(out var vmsg))
                {
                    message = $"레시피 유효성 검사 실패: {vmsg}";
                    return false;
                }

                // Set last recipe and active recipe
                oldRecipe = _activeRecipe;
                _lastRecipe = oldRecipe;
                _activeRecipe = recipe;
                newRecipe = recipe;
                message = "성공";
            }

            // invoke event handlers asynchronously on thread-pool to avoid blocking and improve robustness
            var handlers = ActiveRecipeChanged?.GetInvocationList();
            var args = new RecipeChangedEventArgs(oldRecipe?.ShallowClone(), newRecipe?.ShallowClone());
            if (handlers != null)
            {
                foreach (var d in handlers)
                {
                    if (d is EventHandler<RecipeChangedEventArgs> ev)
                    {
                        Task.Run(() =>
                        {
                            try { ev(this, args); }
                            catch { /* swallow exceptions from subscribers to avoid crashing manager; consider logging */ }
                        });
                    }
                }
            }
            return true;
        }

        // Deactivate current active recipe
        public void DeactivateActiveRecipe()
        {
            RecipeBase oldRecipe = null;
            lock (_lock)
            {
                oldRecipe = _activeRecipe;
                _lastRecipe = _activeRecipe;
                _activeRecipe = null;
            }

            var handlers = ActiveRecipeChanged?.GetInvocationList();
            var args = new RecipeChangedEventArgs(oldRecipe?.ShallowClone(), null);
            if (handlers != null)
            {
                foreach (var d in handlers)
                {
                    if (d is EventHandler<RecipeChangedEventArgs> ev)
                    {
                        Task.Run(() =>
                        {
                            try { ev(this, args); }
                            catch { }
                        });
                    }
                }
            }
        }

        // Active recipe (read-only copy to avoid external mutation)
        public RecipeBase ActiveRecipe
        {
            get
            {
                lock (_lock)
                {
                    return _activeRecipe?.ShallowClone();
                }
            }
        }

        // Last recipe (read-only copy)
        public RecipeBase LastRecipe
        {
            get
            {
                lock (_lock)
                {
                    return _lastRecipe?.ShallowClone();
                }
            }
        }

        // Replace current active recipe with last recipe (undo activation)
        public bool RevertToLastRecipe(out string message)
        {
            RecipeBase oldRecipe = null;
            RecipeBase newRecipe = null;
            lock (_lock)
            {
                if (_lastRecipe == null)
                {
                    message = "이전 활성 레시피가 없습니다.";
                    return false;
                }

                oldRecipe = _activeRecipe;
                _activeRecipe = _lastRecipe;
                newRecipe = _activeRecipe;
                _lastRecipe = null;
                message = "성공";
            }

            var handlers = ActiveRecipeChanged?.GetInvocationList();
            var args = new RecipeChangedEventArgs(oldRecipe?.ShallowClone(), newRecipe?.ShallowClone());
            if (handlers != null)
            {
                foreach (var d in handlers)
                {
                    if (d is EventHandler<RecipeChangedEventArgs> ev)
                    {
                        Task.Run(() =>
                        {
                            try { ev(this, args); }
                            catch { }
                        });
                    }
                }
            }
            return true;
        }

        // Delete recipe: unregister and optionally remove from repository if provided
        public bool DeleteRecipe(string id, RecipeRepository repo, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(id)) { message = "유효하지 않은 레시피 ID입니다."; return false; }

            RecipeBase removed = null;
            lock (_lock)
            {
                if (_recipes.TryGetValue(id, out removed))
                {
                    _recipes.Remove(id);
                }
            }

            // If deleted recipe was active, deactivate and fire event
            if (removed != null)
            {
                bool wasActive = false;
                lock (_lock)
                {
                    wasActive = _activeRecipe != null && string.Equals(_activeRecipe.Id, id, StringComparison.OrdinalIgnoreCase);
                    if (wasActive)
                    {
                        _lastRecipe = _activeRecipe;
                        _activeRecipe = null;
                    }
                }

                if (wasActive)
                {
                    var handlers = ActiveRecipeChanged?.GetInvocationList();
                    var args = new RecipeChangedEventArgs(removed?.ShallowClone(), null);
                    if (handlers != null)
                    {
                        foreach (var d in handlers)
                        {
                            if (d is EventHandler<RecipeChangedEventArgs> ev)
                            {
                                Task.Run(() => { try { ev(this, args); } catch { } });
                            }
                        }
                    }
                }
            }

            // Remove from repository if provided
            if (repo != null)
            {
                try
                {
                    repo.DeleteRecipe(id, out var repoMsg);
                }
                catch { }
            }

            message = "성공";
            return true;
        }

        // Event args for recipe change
        public class RecipeChangedEventArgs : EventArgs
        {
            public RecipeBase OldRecipe { get; }
            public RecipeBase NewRecipe { get; }

            public RecipeChangedEventArgs(RecipeBase oldRecipe, RecipeBase newRecipe)
            {
                OldRecipe = oldRecipe;
                NewRecipe = newRecipe;
            }
        }
    }
}

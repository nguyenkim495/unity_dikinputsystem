using UnityEngine;

namespace Dik.Input
{
    /// <summary>Persists binding overrides as JSON. Assign a custom one to DikInput.BindingStore.</summary>
    public interface IBindingOverrideStore
    {
        string Load(string key);
        void Save(string key, string json);
        void Delete(string key);
    }

    public sealed class PlayerPrefsBindingOverrideStore : IBindingOverrideStore
    {
        public string Load(string key) => PlayerPrefs.GetString(key, null);

        public void Save(string key, string json)
        {
            PlayerPrefs.SetString(key, json);
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}

using System.Collections.Generic;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocAssetLifetime : MonoBehaviour
    {
        private readonly HashSet<Object> resources = new HashSet<Object>();
        public Object[] OwnedResources => new List<Object>(resources).ToArray();

        public void Track(PMXImportResult result)
        {
            if (result == null) return;
            foreach (var mesh in result.meshes) resources.Add(mesh.mesh);
            foreach (var material in result.materials) resources.Add(material);
            foreach (var texture in result.textures) resources.Add(texture);
            if (result.avatarResult != null && result.avatarResult.avatar != null) resources.Add(result.avatarResult.avatar);
        }

        public void Release()
        {
            // Deactivate first: stop playback and let UMT release native solver data.
            gameObject.SetActive(false);
            foreach (var resource in resources) if (resource != null) Destroy(resource);
            resources.Clear();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            foreach (var resource in resources) if (resource != null) Destroy(resource);
            resources.Clear();
        }
    }
}

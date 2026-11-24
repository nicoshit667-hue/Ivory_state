using System.Collections.Generic;
using IvoryState.Client.Characters;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Capture et met en cache un portrait 3D (buste/visage) du personnage à partir du catalogue
    /// d'avatars Rocketbox, utilisé sur la carte d'identité de l'écran Profil.
    /// </summary>
    internal static class PlayerPortraitRenderer
    {
        private const int PortraitSize = 160;
        private static readonly Vector3 StudioOrigin = new Vector3(0f, -2400f, 0f);
        private static readonly Dictionary<int, Sprite> s_cache = new Dictionary<int, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            foreach (Sprite sprite in s_cache.Values)
            {
                if (sprite != null && sprite.texture != null)
                    Object.Destroy(sprite.texture);
                if (sprite != null)
                    Object.Destroy(sprite);
            }
            s_cache.Clear();
        }

        public static Sprite GetPortrait(int targetObjectId, string displayName, byte outfit)
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return null;

            CharacterModels catalog = PlayerAvatarView.Catalog;
            if (catalog == null || catalog.Count == 0)
                return null;

            PlayerNetworkIdentity identity = ResolveIdentity(targetObjectId);
            int model = identity != null && identity.HasAppearance
                ? Mathf.Clamp(identity.Model, 0, catalog.Count - 1)
                : PlayerAvatarView.ModelIndexFor(displayName, catalog.Count);

            int key = (model << 8) | outfit;
            if (s_cache.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            Sprite generated = RenderSnapshot(catalog, model, outfit);
            if (generated != null)
                s_cache[key] = generated;
            return generated;
        }

        private static PlayerNetworkIdentity ResolveIdentity(int targetObjectId)
        {
            if (targetObjectId <= 0)
                return PlayerNetworkIdentity.Local;

            var list = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].ObjectId == targetObjectId)
                    return list[i];
            }
            return null;
        }

        private static Sprite RenderSnapshot(CharacterModels catalog, int model, byte outfit)
        {
            GameObject prefab = catalog.Get(model);
            if (prefab == null)
                return null;

            GameObject avatar = null;
            GameObject camGo = null;
            GameObject lightGo = null;
            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                avatar = Object.Instantiate(prefab, StudioOrigin, Quaternion.identity);
                if (catalog.TryGetOutfit(model, outfit, out Material original, out Material replacement))
                    ApplyOutfitMaterial(avatar, original, replacement);

                Vector3 focus = StudioOrigin + new Vector3(0f, 1.54f, 0f);
                if (avatar.TryGetComponent(out Animator animator) && animator.isHuman)
                {
                    Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                    if (head != null)
                        focus = head.position + new Vector3(0f, 0.03f, 0f);
                }

                camGo = new GameObject("PortraitStudioCam");
                camGo.transform.position = focus + new Vector3(0.06f, 0.03f, 0.62f);
                camGo.transform.LookAt(focus);
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 26f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 4f;
                cam.enabled = false;

                lightGo = new GameObject("PortraitStudioLight");
                lightGo.transform.position = focus + new Vector3(0.6f, 0.9f, 1.1f);
                lightGo.transform.LookAt(focus);
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.97f, 0.93f, 1f);

                rt = RenderTexture.GetTemporary(PortraitSize, PortraitSize, 16, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.ReadPixels(new Rect(0, 0, PortraitSize, PortraitSize), 0, 0);
                tex.Apply();

                return Sprite.Create(tex, new Rect(0, 0, PortraitSize, PortraitSize), new Vector2(0.5f, 0.5f), 100f);
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (lightGo != null) Object.DestroyImmediate(lightGo);
                if (avatar != null) Object.DestroyImmediate(avatar);
            }
        }

        private static void ApplyOutfitMaterial(GameObject avatar, Material original, Material replacement)
        {
            foreach (Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == original)
                    {
                        mats[i] = replacement;
                        changed = true;
                    }
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
        }
    }
}

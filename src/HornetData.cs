using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace HornetInCasualties
{
    // --- manifest shape written by tools/export_hornet_render.py ---

    [Serializable]
    internal class HornetRenderManifest
    {
        public List<HornetMaterialData> materials;
        public Dictionary<string, HornetRenderClipData> clips;
    }

    [Serializable]
    internal class HornetMaterialData
    {
        public string file;
        public int w;
        public int h;
    }

    [Serializable]
    internal class HornetRenderClipData
    {
        public float fps;
        public bool loop;
        public List<HornetFrameData> frames;
    }

    [Serializable]
    internal class HornetFrameData
    {
        public int mat;
        public float[] pos;
        public float[] uv;
    }

    /// <summary>A runnable clip: a mesh per frame plus the material it samples.</summary>
    internal class HornetClip
    {
        public string Name;
        public float Fps;
        public bool Loop;
        public Mesh[] Meshes;
        public int[] Materials;
    }

    /// <summary>
    /// Loads Hornet's atlas + per-frame render quads from the plugin's data folder
    /// (extracted from the user's own Silksong install by tools/export_hornet_render.py).
    /// Frames are drawn as quads, exactly as tk2d stores them, so rotated/flipped
    /// atlas packing needs no reconstruction.
    /// </summary>
    internal static class HornetSprites
    {
        private static Dictionary<string, HornetClip> _clips;
        private static Texture2D[] _textures;
        private static Material[] _materials;

        public static Dictionary<string, HornetClip> Clips
        {
            get { return _clips; }
        }

        public static bool Ready
        {
            get { return _clips != null && _clips.Count > 0; }
        }

        public static Material MaterialFor(int index)
        {
            if (_materials == null || index < 0 || index >= _materials.Length)
            {
                return null;
            }
            return _materials[index];
        }

        public static void Load(string pluginDir)
        {
            if (_clips != null)
            {
                return;
            }

            try
            {
                LoadInternal(pluginDir);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("HornetSprites.Load failed: " + e);
                _clips = new Dictionary<string, HornetClip>();
            }
        }

        private static void LoadInternal(string pluginDir)
        {
            _clips = new Dictionary<string, HornetClip>();
            _textures = new Texture2D[0];
            _materials = new Material[0];

            string dataDir = Path.Combine(pluginDir, "hornet");
            string manifestPath = Path.Combine(dataDir, "render.json");
            if (!File.Exists(manifestPath))
            {
                Plugin.Log.LogWarning("Hornet render data not found at " + manifestPath +
                                      " (run tools/export_hornet_render.py). Using placeholder.");
                return;
            }

            HornetRenderManifest manifest =
                JsonConvert.DeserializeObject<HornetRenderManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.clips == null)
            {
                Plugin.Log.LogError("render.json was empty or malformed.");
                return;
            }

            // Textures + one material each. Base it on the game's own sprite material so it
            // works with whatever render pipeline the game uses.
            List<HornetMaterialData> mats = manifest.materials ?? new List<HornetMaterialData>();
            _textures = new Texture2D[mats.Count];
            _materials = new Material[mats.Count];
            Material template = WorldGeneration.world != null ? WorldGeneration.world.defaultMat : null;
            for (int i = 0; i < mats.Count; i++)
            {
                string path = Path.Combine(dataDir, mats[i].file);
                if (!File.Exists(path))
                {
                    Plugin.Log.LogError("missing Hornet atlas: " + path);
                    continue;
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                tex.LoadImage(File.ReadAllBytes(path));
                _textures[i] = tex;

                Material mat = null;
                if (template != null)
                {
                    mat = new Material(template);
                }
                else
                {
                    Shader shader = Shader.Find("Sprites/Default")
                                    ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                                    ?? Shader.Find("Unlit/Transparent");
                    if (shader != null)
                    {
                        mat = new Material(shader);
                    }
                }

                if (mat != null)
                {
                    mat.mainTexture = tex;
                }
                _materials[i] = mat;
            }

            foreach (KeyValuePair<string, HornetRenderClipData> kv in manifest.clips)
            {
                HornetRenderClipData cd = kv.Value;
                if (cd == null || cd.frames == null || cd.frames.Count == 0)
                {
                    continue;
                }

                var clip = new HornetClip
                {
                    Name = kv.Key,
                    Fps = cd.fps <= 0.01f ? 12f : cd.fps,
                    Loop = cd.loop,
                    Meshes = new Mesh[cd.frames.Count],
                    Materials = new int[cd.frames.Count],
                };
                for (int i = 0; i < cd.frames.Count; i++)
                {
                    clip.Meshes[i] = BuildQuad(cd.frames[i]);
                    clip.Materials[i] = cd.frames[i].mat;
                }
                _clips[kv.Key] = clip;
            }

            Plugin.Log.LogInfo("Loaded " + _clips.Count + " Hornet clips, " + mats.Count + " atlas texture(s).");
        }

        private static Mesh BuildQuad(HornetFrameData f)
        {
            float[] p = f.pos;
            float[] t = f.uv;
            var verts = new Vector3[4];
            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                verts[i] = new Vector3(p[i * 2], p[i * 2 + 1], 0f);
                uvs[i] = new Vector2(t[i * 2], t[i * 2 + 1]);
            }

            var mesh = new Mesh { name = "HornetFrame" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Plays a named Hornet clip as animated quads.</summary>
    public class HornetMeshAnimator : MonoBehaviour
    {
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private HornetClip _clip;
        private float _accum;
        private int _frame;

        public void Init(MeshFilter filter, MeshRenderer renderer)
        {
            _filter = filter;
            _renderer = renderer;
        }

        public void Play(string name, bool restart = false)
        {
            if (HornetSprites.Clips == null)
            {
                return;
            }

            if (!restart && _clip != null && _clip.Name == name)
            {
                return;
            }

            HornetClip c;
            if (!HornetSprites.Clips.TryGetValue(name, out c))
            {
                return;
            }

            _clip = c;
            _frame = 0;
            _accum = 0f;
            Apply();
        }

        private void Update()
        {
            if (_clip == null || _clip.Meshes.Length == 0)
            {
                return;
            }

            _accum += Time.deltaTime;
            float spf = 1f / _clip.Fps;
            bool changed = false;
            while (_accum >= spf)
            {
                _accum -= spf;
                _frame++;
                if (_frame >= _clip.Meshes.Length)
                {
                    _frame = _clip.Loop ? 0 : _clip.Meshes.Length - 1;
                }
                changed = true;
            }

            if (changed)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (_filter == null || _clip == null || _clip.Meshes.Length == 0)
            {
                return;
            }

            _filter.sharedMesh = _clip.Meshes[_frame];
            if (_renderer != null)
            {
                Material m = HornetSprites.MaterialFor(_clip.Materials[_frame]);
                if (m != null)
                {
                    _renderer.sharedMaterial = m;
                }
            }
        }
    }
}

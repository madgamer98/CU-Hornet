using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace HornetInCasualties
{
    // --- manifest shape written by the Silksong HornetExporter (bake.json) ---

    [Serializable]
    internal class BakeManifest
    {
        public Dictionary<string, BakeClip> frames;
    }

    [Serializable]
    internal class BakeClip
    {
        public float fps;
        public List<BakeFrame> frames;
    }

    [Serializable]
    internal class BakeFrame
    {
        public string file;
        public int w;
        public int h;
        public float px;
        public float py;
    }

    /// <summary>A runnable clip: sprites plus timing.</summary>
    internal class HornetClip
    {
        public string Name;
        public float Fps;
        public bool Loop;
        public Sprite[] Frames;
    }

    /// <summary>
    /// Loads Hornet frames baked from the user's own Silksong install by the Silksong-side
    /// HornetExporter plugin (`bake.json` + PNGs). Nothing is shipped with this mod.
    /// </summary>
    internal static class HornetSprites
    {
        private static Dictionary<string, HornetClip> _clips;

        public static Dictionary<string, HornetClip> Clips
        {
            get { return _clips; }
        }

        public static bool Ready
        {
            get { return _clips != null && _clips.Count > 0; }
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
            catch (Exception e)
            {
                Plugin.Log.LogError("HornetSprites.Load failed: " + e);
                _clips = new Dictionary<string, HornetClip>();
            }
        }

        private static void LoadInternal(string pluginDir)
        {
            _clips = new Dictionary<string, HornetClip>();
            string dataDir = Path.Combine(pluginDir, "hornet");
            string manifestPath = Path.Combine(dataDir, "bake.json");
            if (!File.Exists(manifestPath))
            {
                Plugin.Log.LogWarning("Hornet bake data not found at " + manifestPath + ".");
                return;
            }

            BakeManifest manifest = JsonConvert.DeserializeObject<BakeManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.frames == null)
            {
                Plugin.Log.LogError("bake.json was empty or malformed.");
                return;
            }

            float ppu = Mathf.Max(1f, Plugin.AvatarPpu.Value);
            foreach (KeyValuePair<string, BakeClip> kv in manifest.frames)
            {
                BakeClip cd = kv.Value;
                if (cd == null || cd.frames == null || cd.frames.Count == 0)
                {
                    continue;
                }
                var clip = new HornetClip
                {
                    Name = kv.Key,
                    Fps = cd.fps <= 0.01f ? 12f : cd.fps,
                    Loop = IsLooping(kv.Key),
                    Frames = new Sprite[cd.frames.Count],
                };
                for (int i = 0; i < cd.frames.Count; i++)
                {
                    BakeFrame f = cd.frames[i];
                    string path = Path.Combine(dataDir, f.file.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path))
                    {
                        Plugin.Log.LogError("missing Hornet frame: " + path);
                        continue;
                    }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    tex.LoadImage(File.ReadAllBytes(path));
                    clip.Frames[i] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(f.px, f.py), ppu);
                }
                _clips[kv.Key] = clip;
            }

            Plugin.Log.LogInfo("Loaded " + _clips.Count + " Hornet clips from bake.json.");
        }

        private static bool IsLooping(string name)
        {
            switch (name)
            {
                case "Idle":
                case "Run":
                case "Airborne":
                case "Wall Scramble":
                case "Sprint":
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Plays a named Hornet clip frame by frame on a SpriteRenderer.</summary>
    public class HornetAnimator : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private HornetClip _clip;
        private float _accum;
        private int _frame;

        public void Init(SpriteRenderer renderer)
        {
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
            if (_clip == null || _clip.Frames.Length == 0)
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
                if (_frame >= _clip.Frames.Length)
                {
                    _frame = _clip.Loop ? 0 : _clip.Frames.Length - 1;
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
            if (_renderer != null && _clip != null && _clip.Frames.Length > 0 && _clip.Frames[_frame] != null)
            {
                _renderer.sprite = _clip.Frames[_frame];
            }
        }
    }
}

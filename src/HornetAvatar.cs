using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Thin marker on the player that attaches the passthrough <see cref="LiveLink"/>.
    /// The old baked-art port has been removed; Hornet is rendered by Silksong.
    /// </summary>
    public class HornetAvatar : MonoBehaviour
    {
        public static HornetAvatar Ensure(Body body)
        {
            if (body == null)
            {
                return null;
            }
            HornetAvatar existing = body.GetComponent<HornetAvatar>();
            if (existing != null)
            {
                return existing;
            }

            HornetAvatar avatar = body.gameObject.AddComponent<HornetAvatar>();
            if (Plugin.LiveMode.Value)
            {
                try
                {
                    LiveLink link = body.gameObject.GetComponent<LiveLink>();
                    if (link == null)
                    {
                        link = body.gameObject.AddComponent<LiveLink>();
                    }
                    link.Init(body);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError("LiveLink init failed: " + e);
                }
            }
            return avatar;
        }
    }
}

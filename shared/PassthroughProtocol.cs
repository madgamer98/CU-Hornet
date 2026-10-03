using System;
using System.IO.MemoryMappedFiles;

namespace HornetPassthrough
{
    /// <summary>
    /// Shared-memory protocol between Casualties: Unknown (host) and Hollow Knight: Silksong
    /// (Hornet source). One named mapping, single writer per region:
    ///   CU    writes CuState + Input (its player state / raw buttons) and reads the frame
    ///   Silk  writes Frame (Hornet RGBA + pivot)                     and reads CuState + Input
    /// A sequence counter guards the frame against tearing. The Input region carries the host's
    /// raw buttons so Silksong's real controller can be driven by them (S0: input round-trip).
    /// </summary>
    public static class Proto
    {
        public const string MappingName = "Local\\HornetPassthrough_v1";
        public const uint Magic = 0x48505431; // "HPT1"
        public const int Version = 4;

        public const int HeaderOffset = 0;
        public const int HeaderSize = 64;
        public const int CuStateOffset = 64;
        public const int CuStateSize = 64;
        public const int FrameMetaOffset = 128;
        public const int FrameMetaSize = 64;
        public const int PixelsOffset = 192;
        public const int MaxWidth = 384;
        public const int MaxHeight = 384;
        public const int PixelsSize = MaxWidth * MaxHeight * 4;
        public const int InputOffset = PixelsOffset + PixelsSize;
        public const int InputSize = 32;
        public const int TerrainOffset = InputOffset + InputSize;
        public const int TerrainHeaderSize = 16;
        public const int MaxRects = 256;
        public const int TerrainRectsOffset = TerrainOffset + TerrainHeaderSize;
        public const int TerrainSize = TerrainHeaderSize + MaxRects * 16;
        public const int PlayerStateOffset = TerrainOffset + TerrainSize;
        public const int PlayerStateSize = 64;
        public const long MappingSize = PlayerStateOffset + PlayerStateSize;

        // Raw button bits (Input region, CU -> Silksong). Mirrors the host's real binds.
        public const int BtnLeft = 1 << 0;
        public const int BtnRight = 1 << 1;
        public const int BtnUp = 1 << 2;
        public const int BtnDown = 1 << 3;
        public const int BtnJump = 1 << 4;
        public const int BtnAttack = 1 << 5;
        public const int BtnDash = 1 << 6;
        public const int BtnNeedle = 1 << 7; // Hornet's ranged/cast action
        public const int BtnEnabled = 1 << 8; // host is actively forwarding input

        // Header fields
        public const int HO_Magic = 0;
        public const int HO_Version = 4;
        public const int HO_CuPid = 8;
        public const int HO_SilkPid = 12;
        public const int HO_CuHeartbeat = 16; // long ms
        public const int HO_SilkHeartbeat = 24;

        // CuState flags bits
        public const int FlagEnabled = 1 << 0;
        public const int FlagAttack = 1 << 1;
        public const int FlagDash = 1 << 2;
        public const int FlagNeedle = 1 << 3;
        public const int FlagUp = 1 << 4;
        public const int FlagDown = 1 << 5;

        // CuState (CU -> Silksong)
        public const int CO_PosX = 0;
        public const int CO_PosY = 4;
        public const int CO_VelX = 8;
        public const int CO_VelY = 12;
        public const int CO_Facing = 16;   // +1 right, -1 left
        public const int CO_Grounded = 20; // 0/1
        public const int CO_Flags = 24;    // bit0: enabled, bit1: attack, bit2: dash, bit3: needle

        // FrameMeta (Silksong -> CU)
        public const int FO_Seq = 0;       // int, odd while writing
        public const int FO_Width = 4;
        public const int FO_Height = 8;
        public const int FO_PivotX = 12;   // float, normalized origin within the crop
        public const int FO_PivotY = 16;
        public const int FO_WorldX = 20;
        public const int FO_WorldY = 24;
        public const int FO_ClipHash = 28; // stable hash of the current clip name
        public const int FO_FrameId = 32;  // increments each published frame
        public const int FO_FrameIndex = 36; // frame within the current clip
        public const int FO_Facing = 40;     // +1 right, -1 left
        public const int FO_HasPixels = 44;  // 1 when the pixel buffer is valid
        public const int FO_Name = 48;       // up to 16 ASCII chars (clip name), NUL padded

        // Input (CU -> Silksong): raw buttons + a small sequence counter.
        public const int IO_Buttons = 0;     // int bitfield of Btn* above
        public const int IO_Seq = 4;         // int, odd while writing

        // Terrain (CU -> Silksong): a window of ground AABBs relative to the CU player's ground
        // contact, in CU world units. count rects at TerrainRectsOffset, each (x, y, w, h) float32.
        public const int TH_Revision = 0;    // int, bumps when the window changes
        public const int TH_Count = 4;
        public const int TH_PlayerHeight = 8; // float, CU player collider height (scale reference)
        public const int TH_Seq = 12;        // int seqlock

        // PlayerState (Silksong -> CU): Hornet's state mapped back into CU coordinates by the
        // fixed origin+scale captured when the terrain mirror was applied.
        public const int PS_PosX = 0;        // float, CU-space position CU should adopt
        public const int PS_PosY = 4;
        public const int PS_VelX = 8;        // float, CU-space velocity
        public const int PS_VelY = 12;
        public const int PS_Facing = 16;     // +1 right, -1 left
        public const int PS_Grounded = 20;   // 0/1
        public const int PS_Active = 24;     // 1 when the mirror/mapping is live
        public const int PS_Seq = 28;        // int seqlock
    }

    /// <summary>Thin wrapper over the mapping with float/int helpers.</summary>
    public sealed class PassthroughLink : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _view;
        public readonly bool IsHost;
        public readonly int Pid;

        public PassthroughLink(bool create)
        {
            IsHost = create;
            Pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            _mmf = create
                ? MemoryMappedFile.CreateOrOpen(Proto.MappingName, Proto.MappingSize)
                : MemoryMappedFile.OpenExisting(Proto.MappingName);
            _view = _mmf.CreateViewAccessor(0, Proto.MappingSize);

            if (create)
            {
                WriteInt(Proto.HeaderOffset + Proto.HO_Magic, unchecked((int)Proto.Magic));
                WriteInt(Proto.HeaderOffset + Proto.HO_Version, Proto.Version);
                WriteInt(Proto.HeaderOffset + Proto.HO_CuPid, Pid);
            }
            else
            {
                WriteInt(Proto.HeaderOffset + Proto.HO_SilkPid, Pid);
            }
            Heartbeat();
        }

        public void Heartbeat()
        {
            long ms = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            if (IsHost)
            {
                WriteLong(Proto.HeaderOffset + Proto.HO_CuHeartbeat, ms);
            }
            else
            {
                WriteLong(Proto.HeaderOffset + Proto.HO_SilkHeartbeat, ms);
            }
        }

        // ---- CuState ----
        public void WriteCuState(float x, float y, float vx, float vy, int facing, bool grounded, int flags)
        {
            WriteFloat(Proto.CuStateOffset + Proto.CO_PosX, x);
            WriteFloat(Proto.CuStateOffset + Proto.CO_PosY, y);
            WriteFloat(Proto.CuStateOffset + Proto.CO_VelX, vx);
            WriteFloat(Proto.CuStateOffset + Proto.CO_VelY, vy);
            WriteInt(Proto.CuStateOffset + Proto.CO_Facing, facing);
            WriteInt(Proto.CuStateOffset + Proto.CO_Grounded, grounded ? 1 : 0);
            WriteInt(Proto.CuStateOffset + Proto.CO_Flags, flags);
        }

        public void ReadCuState(out float x, out float y, out float vx, out float vy, out int facing,
            out bool grounded, out int flags)
        {
            x = ReadFloat(Proto.CuStateOffset + Proto.CO_PosX);
            y = ReadFloat(Proto.CuStateOffset + Proto.CO_PosY);
            vx = ReadFloat(Proto.CuStateOffset + Proto.CO_VelX);
            vy = ReadFloat(Proto.CuStateOffset + Proto.CO_VelY);
            facing = ReadInt(Proto.CuStateOffset + Proto.CO_Facing);
            grounded = ReadInt(Proto.CuStateOffset + Proto.CO_Grounded) != 0;
            flags = ReadInt(Proto.CuStateOffset + Proto.CO_Flags);
        }

        // ---- Input (CU -> Silksong) ----
        /// <summary>Publish the host's raw button bitfield.</summary>
        public void WriteInput(int buttons)
        {
            int seq = ReadInt(Proto.InputOffset + Proto.IO_Seq);
            WriteInt(Proto.InputOffset + Proto.IO_Seq, seq + 1); // odd = writing
            WriteInt(Proto.InputOffset + Proto.IO_Buttons, buttons);
            WriteInt(Proto.InputOffset + Proto.IO_Seq, seq + 2); // even = stable
        }

        /// <summary>Read the host's raw button bitfield.</summary>
        public int ReadInput()
        {
            int seq1 = ReadInt(Proto.InputOffset + Proto.IO_Seq);
            if ((seq1 & 1) != 0)
            {
                return 0; // writer mid-publish; caller retries next frame
            }
            int buttons = ReadInt(Proto.InputOffset + Proto.IO_Buttons);
            int seq2 = ReadInt(Proto.InputOffset + Proto.IO_Seq);
            return seq1 == seq2 ? buttons : 0;
        }

        // ---- Terrain (CU -> Silksong) ----
        /// <summary>Publish ground AABBs relative to (anchorX, anchorY), in CU units. rects is x,y,w,h per item.</summary>
        public void WriteTerrain(float playerHeight, float[] rects, int count, int revision)
        {
            int seq = ReadInt(Proto.TerrainOffset + Proto.TH_Seq);
            WriteInt(Proto.TerrainOffset + Proto.TH_Seq, seq + 1);
            WriteInt(Proto.TerrainOffset + Proto.TH_Revision, revision);
            WriteInt(Proto.TerrainOffset + Proto.TH_Count, count);
            WriteFloat(Proto.TerrainOffset + Proto.TH_PlayerHeight, playerHeight);

            int n = System.Math.Min(count, Proto.MaxRects);
            for (int i = 0; i < n; i++)
            {
                int at = Proto.TerrainRectsOffset + i * 16;
                WriteFloat(at + 0, rects[i * 4 + 0]);
                WriteFloat(at + 4, rects[i * 4 + 1]);
                WriteFloat(at + 8, rects[i * 4 + 2]);
                WriteFloat(at + 12, rects[i * 4 + 3]);
            }
            WriteInt(Proto.TerrainOffset + Proto.TH_Seq, seq + 2);
        }

        /// <summary>Read the terrain window. Returns true when a new revision was consumed.</summary>
        public bool ReadTerrain(float[] rects, out int count, out int revision, out float playerHeight,
            ref int lastRevision)
        {
            count = 0;
            revision = ReadInt(Proto.TerrainOffset + Proto.TH_Revision);
            playerHeight = ReadFloat(Proto.TerrainOffset + Proto.TH_PlayerHeight);
            if (revision == lastRevision)
            {
                return false;
            }
            int seq1 = ReadInt(Proto.TerrainOffset + Proto.TH_Seq);
            if ((seq1 & 1) != 0)
            {
                return false; // writer mid-publish
            }
            count = ReadInt(Proto.TerrainOffset + Proto.TH_Count);
            playerHeight = ReadFloat(Proto.TerrainOffset + Proto.TH_PlayerHeight);
            int n = System.Math.Min(count, Proto.MaxRects);
            for (int i = 0; i < n; i++)
            {
                int at = Proto.TerrainRectsOffset + i * 16;
                rects[i * 4 + 0] = ReadFloat(at + 0);
                rects[i * 4 + 1] = ReadFloat(at + 4);
                rects[i * 4 + 2] = ReadFloat(at + 8);
                rects[i * 4 + 3] = ReadFloat(at + 12);
            }
            int seq2 = ReadInt(Proto.TerrainOffset + Proto.TH_Seq);
            if (seq1 != seq2)
            {
                return false; // torn, retry next frame
            }
            count = n;
            lastRevision = revision;
            return true;
        }

        // ---- PlayerState (Silksong -> CU) ----
        public void WritePlayerState(float x, float y, float vx, float vy, int facing, bool grounded, bool active)
        {
            int seq = ReadInt(Proto.PlayerStateOffset + Proto.PS_Seq);
            WriteInt(Proto.PlayerStateOffset + Proto.PS_Seq, seq + 1);
            WriteFloat(Proto.PlayerStateOffset + Proto.PS_PosX, x);
            WriteFloat(Proto.PlayerStateOffset + Proto.PS_PosY, y);
            WriteFloat(Proto.PlayerStateOffset + Proto.PS_VelX, vx);
            WriteFloat(Proto.PlayerStateOffset + Proto.PS_VelY, vy);
            WriteInt(Proto.PlayerStateOffset + Proto.PS_Facing, facing);
            WriteInt(Proto.PlayerStateOffset + Proto.PS_Grounded, grounded ? 1 : 0);
            WriteInt(Proto.PlayerStateOffset + Proto.PS_Active, active ? 1 : 0);
            WriteInt(Proto.PlayerStateOffset + Proto.PS_Seq, seq + 2);
        }

        public bool ReadPlayerState(out float x, out float y, out float vx, out float vy, out int facing,
            out bool grounded, out bool active)
        {
            x = y = vx = vy = 0f;
            facing = 0;
            grounded = false;
            active = false;
            int seq1 = ReadInt(Proto.PlayerStateOffset + Proto.PS_Seq);
            if ((seq1 & 1) != 0)
            {
                return false;
            }
            x = ReadFloat(Proto.PlayerStateOffset + Proto.PS_PosX);
            y = ReadFloat(Proto.PlayerStateOffset + Proto.PS_PosY);
            vx = ReadFloat(Proto.PlayerStateOffset + Proto.PS_VelX);
            vy = ReadFloat(Proto.PlayerStateOffset + Proto.PS_VelY);
            facing = ReadInt(Proto.PlayerStateOffset + Proto.PS_Facing);
            grounded = ReadInt(Proto.PlayerStateOffset + Proto.PS_Grounded) != 0;
            active = ReadInt(Proto.PlayerStateOffset + Proto.PS_Active) != 0;
            int seq2 = ReadInt(Proto.PlayerStateOffset + Proto.PS_Seq);
            return seq1 == seq2;
        }

        // ---- Frame ----
        /// <summary>Publish a frame (Silksong side). pixels is RGBA32, bottom-up (Unity order).</summary>
        public void WriteFrame(byte[] pixels, int width, int height, float pivotX, float pivotY,
            float worldX, float worldY, int clipHash)
        {
            int seq = ReadInt(Proto.FrameMetaOffset + Proto.FO_Seq);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_Seq, seq + 1); // odd = writing
            if (pixels != null)
            {
                int n = System.Math.Min(pixels.Length, Proto.PixelsSize);
                _view.WriteArray(Proto.PixelsOffset, pixels, 0, n);
            }
            WriteInt(Proto.FrameMetaOffset + Proto.FO_HasPixels, pixels != null ? 1 : 0);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_Width, width);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_Height, height);
            WriteFloat(Proto.FrameMetaOffset + Proto.FO_PivotX, pivotX);
            WriteFloat(Proto.FrameMetaOffset + Proto.FO_PivotY, pivotY);
            WriteFloat(Proto.FrameMetaOffset + Proto.FO_WorldX, worldX);
            WriteFloat(Proto.FrameMetaOffset + Proto.FO_WorldY, worldY);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_ClipHash, clipHash);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_FrameId, ReadInt(Proto.FrameMetaOffset + Proto.FO_FrameId) + 1);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_Seq, seq + 2); // even = stable
        }

        /// <summary>Publish Hornet's live animator state (Silksong side). name up to 15 chars.</summary>
        public void WriteLive(string clipName, int frameIndex, int facing, int frameId)
        {
            int seq = ReadInt(Proto.FrameMetaOffset + Proto.FO_Seq);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_Seq, seq + 1);
            WriteName(clipName);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_FrameIndex, frameIndex);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_Facing, facing);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_HasPixels, 0);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_FrameId, frameId);
            WriteInt(Proto.FrameMetaOffset + Proto.FO_Seq, seq + 2);
        }

        public void ReadLive(out string clipName, out int frameIndex, out int facing, out int frameId,
            out bool hasPixels)
        {
            clipName = ReadName();
            frameIndex = ReadInt(Proto.FrameMetaOffset + Proto.FO_FrameIndex);
            facing = ReadInt(Proto.FrameMetaOffset + Proto.FO_Facing);
            frameId = ReadInt(Proto.FrameMetaOffset + Proto.FO_FrameId);
            hasPixels = ReadInt(Proto.FrameMetaOffset + Proto.FO_HasPixels) != 0;
        }

        private void WriteName(string name)
        {
            var buf = new byte[16];
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < buf.Length - 1 && i < name.Length; i++)
                {
                    buf[i] = (byte)name[i];
                }
            }
            _view.WriteArray(Proto.FrameMetaOffset + Proto.FO_Name, buf, 0, buf.Length);
        }

        private string ReadName()
        {
            var buf = new byte[16];
            _view.ReadArray(Proto.FrameMetaOffset + Proto.FO_Name, buf, 0, buf.Length);
            int len = 0;
            while (len < buf.Length && buf[len] != 0)
            {
                len++;
            }
            return System.Text.Encoding.ASCII.GetString(buf, 0, len);
        }

        /// <summary>Read the latest frame if it changed (CU side). Returns true when updated.</summary>
        public bool ReadFrame(byte[] pixels, out int width, out int height, out float pivotX, out float pivotY,
            out float worldX, out float worldY, out int frameId, ref int lastFrameId)
        {
            width = height = 0;
            pivotX = pivotY = worldX = worldY = 0f;
            frameId = ReadInt(Proto.FrameMetaOffset + Proto.FO_FrameId);
            if (frameId == lastFrameId)
            {
                return false;
            }
            int seq1 = ReadInt(Proto.FrameMetaOffset + Proto.FO_Seq);
            if ((seq1 & 1) != 0)
            {
                return false; // writer mid-publish
            }
            width = ReadInt(Proto.FrameMetaOffset + Proto.FO_Width);
            height = ReadInt(Proto.FrameMetaOffset + Proto.FO_Height);
            pivotX = ReadFloat(Proto.FrameMetaOffset + Proto.FO_PivotX);
            pivotY = ReadFloat(Proto.FrameMetaOffset + Proto.FO_PivotY);
            worldX = ReadFloat(Proto.FrameMetaOffset + Proto.FO_WorldX);
            worldY = ReadFloat(Proto.FrameMetaOffset + Proto.FO_WorldY);
            int n = System.Math.Min(width * height * 4, Proto.PixelsSize);
            if (n > 0)
            {
                _view.ReadArray(Proto.PixelsOffset, pixels, 0, n);
            }
            int seq2 = ReadInt(Proto.FrameMetaOffset + Proto.FO_Seq);
            if (seq1 != seq2)
            {
                return false; // torn, retry next frame
            }
            lastFrameId = frameId;
            return true;
        }

        public int DebugFrameId { get { return ReadInt(Proto.FrameMetaOffset + Proto.FO_FrameId); } }
        public int DebugWidth { get { return ReadInt(Proto.FrameMetaOffset + Proto.FO_Width); } }
        public int DebugSeq { get { return ReadInt(Proto.FrameMetaOffset + Proto.FO_Seq); } }
        public int DebugHasPixels { get { return ReadInt(Proto.FrameMetaOffset + Proto.FO_HasPixels); } }

        public bool SilkAlive(int withinMs = 1500)
        {
            long hb = ReadLong(Proto.HeaderOffset + Proto.HO_SilkHeartbeat);
            long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            return hb > 0 && (now - hb) < withinMs;
        }

        private void WriteInt(int at, int v) { _view.Write(at, v); }
        private int ReadInt(int at) { return _view.ReadInt32(at); }
        private void WriteLong(int at, long v) { _view.Write(at, v); }
        private long ReadLong(int at) { return _view.ReadInt64(at); }
        private void WriteFloat(int at, float v) { _view.Write(at, v); }
        private float ReadFloat(int at) { return _view.ReadSingle(at); }

        public void Dispose()
        {
            _view.Dispose();
            _mmf.Dispose();
        }
    }

}

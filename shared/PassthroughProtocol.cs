using System;
using System.IO.MemoryMappedFiles;

namespace HornetPassthrough
{
    /// <summary>
    /// Shared-memory protocol between Casualties: Unknown (host) and Hollow Knight: Silksong
    /// (Hornet source). One named mapping, single writer per region:
    ///   CU    writes CuState   (its player state)          and reads the frame
    ///   Silk  writes Frame     (Hornet RGBA + pivot)        and reads CuState
    /// A sequence counter guards the frame against tearing.
    /// </summary>
    public static class Proto
    {
        public const string MappingName = "Local\\HornetPassthrough_v1";
        public const uint Magic = 0x48505431; // "HPT1"
        public const int Version = 1;

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
        public const long MappingSize = PixelsOffset + PixelsSize;

        // Header fields
        public const int HO_Magic = 0;
        public const int HO_Version = 4;
        public const int HO_CuPid = 8;
        public const int HO_SilkPid = 12;
        public const int HO_CuHeartbeat = 16; // long ms
        public const int HO_SilkHeartbeat = 24;

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

﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

namespace StudioCore
{
    unsafe class Tracy
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct ___tracy_source_location_data
        {
            public char* name;
            public char* function;
            public char* file;
            public uint line;
            public uint color;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct ___tracy_c_zone_context
        {
            public uint id;
            public int active;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct ___tracy_gpu_time_data
        {
            public long gpuTime;
            public ushort queryId;
            public byte context;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct ___tracy_gpu_zone_begin_data
        {
            public ulong srcloc;
            public ushort queryId;
            public byte context;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct ___tracy_gpu_zone_end_data
        {
            public ushort queryId;
            public byte context;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct ___tracy_gpu_new_context_data
        {
            public long gpuTime;
            public float period;
            public byte context;
            public byte flags;
            public byte type;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct ___tracy_gpu_context_name_data
        {
            public byte context;
            [MarshalAs(UnmanagedType.LPStr)]
            public string name;
            public ushort len;
        }

        public class TracyCZoneCtx
        {
            public TracyCZoneCtx(___tracy_c_zone_context val)
            {
                _ctx = val;
            }

            internal ___tracy_c_zone_context _ctx;
        }

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_startup_profiler();

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_shutdown_profiler();

        public static void Startup()
        {
            ___tracy_startup_profiler();
        }

        public static void Shutdown()
        {
            ___tracy_shutdown_profiler();
        }

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ___tracy_c_zone_context ___tracy_emit_zone_begin(___tracy_source_location_data* srcloc, int active );

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ___tracy_c_zone_context ___tracy_emit_zone_begin_callstack(___tracy_source_location_data* srcloc, int depth, int active);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ___tracy_c_zone_context ___tracy_emit_zone_begin_alloc(ulong srcloc, int active);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ___tracy_c_zone_context ___tracy_emit_zone_begin_alloc_callstack(ulong srcloc, int depth, int active);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_zone_end(___tracy_c_zone_context ctx);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_zone_text(___tracy_c_zone_context ctx, string txt, ulong size);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_zone_name(___tracy_c_zone_context ctx, string txt, ulong size);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_zone_color(___tracy_c_zone_context ctx, uint color);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_zone_value(___tracy_c_zone_context ctx, ulong value);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong ___tracy_alloc_srcloc(uint line, string source, ulong sourceSz, string function, ulong functionSz);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong ___tracy_alloc_srcloc_name(uint line, string source, ulong sourceSz, string function, ulong functionSz, string name, ulong nameSz);

        public static ___tracy_c_zone_context TracyCZone(int active,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            ___tracy_c_zone_context result;
            //var m = Marshal.StringToHGlobalAnsi(memberName);
            //var f = Marshal.StringToHGlobalAnsi(sourceFilePath);
            var id = ___tracy_alloc_srcloc((uint)sourceLineNumber, sourceFilePath, (ulong)sourceFilePath.Length, memberName, (ulong)memberName.Length);
            result = ___tracy_emit_zone_begin_alloc(id, active);

            //Marshal.FreeHGlobal(m);
            //Marshal.FreeHGlobal(f);
            return result;
        }

        public static ___tracy_c_zone_context TracyCZoneN(int active,
            string name,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            ___tracy_c_zone_context result;
            var id = ___tracy_alloc_srcloc_name((uint)sourceLineNumber, sourceFilePath, (ulong)sourceFilePath.Length, memberName, (ulong)memberName.Length, name, (ulong)name.Length);
            result = ___tracy_emit_zone_begin_alloc(id, active);
            return result;
        }

        public static ___tracy_c_zone_context TracyCZoneC(int active,
            uint color,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            ___tracy_c_zone_context result;
            var id = ___tracy_alloc_srcloc((uint)sourceLineNumber, sourceFilePath, (ulong)sourceFilePath.Length, memberName, (ulong)memberName.Length);
            result = ___tracy_emit_zone_begin_alloc(id, active);
            ___tracy_emit_zone_color(result, color);
            return result;
        }

        public static ___tracy_c_zone_context TracyCZoneNC(int active,
            string name,
            uint color,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            ___tracy_c_zone_context result;
            var id = ___tracy_alloc_srcloc_name((uint)sourceLineNumber, sourceFilePath, (ulong)sourceFilePath.Length, memberName, (ulong)memberName.Length, name, (ulong)name.Length);
            result = ___tracy_emit_zone_begin_alloc(id, active);
            ___tracy_emit_zone_color(result, color);
            return result;
        }

        public static void TracyCZoneEnd(___tracy_c_zone_context ctx)
        {
            ___tracy_emit_zone_end(ctx);
        }

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_frame_mark(string name);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_frame_mark_start(string name);

        [DllImport("tracy.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ___tracy_emit_frame_mark_end(string name);

        public static void TracyCFrameMark()
        {
            ___tracy_emit_frame_mark(null);
        }

        public static void TracyCFrameMarkNamed(string name)
        {
            ___tracy_emit_frame_mark(name);
        }

        public static void TracyCFrameMarkStart(string name)
        {
            ___tracy_emit_frame_mark_start(name);
        }

        public static void TracyCFrameMarkEnd(string name)
        {
            ___tracy_emit_frame_mark_end(name);
        }
    }
}

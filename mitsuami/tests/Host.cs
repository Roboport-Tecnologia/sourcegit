using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Tests
{
    /// <summary>The headless UI the library has with its <c>test-host</c> feature
    /// (<c>codeview/src/test_host.rs</c>), and what tests wait with.</summary>
    public static unsafe class Host
    {
        private static delegate* unmanaged[Cdecl]<byte> s_start;
        private static delegate* unmanaged[Cdecl]<void> s_settle;
        private static delegate* unmanaged[Cdecl]<uint, Str, uint> s_find;
        private static delegate* unmanaged[Cdecl]<uint, byte> s_activate;
        private static delegate* unmanaged[Cdecl]<uint, byte> s_close;
        private static delegate* unmanaged[Cdecl]<uint, Str, byte> s_command;

        [StructLayout(LayoutKind.Sequential)]
        private struct Str
        {
            public byte* Ptr;
            public nuint Len;
        }

        public static void Start()
        {
            var file = Environment.GetEnvironmentVariable("MITSUAMI_FFI")
                ?? throw new InvalidOperationException("MITSUAMI_FFI names the library: run codeview/test.sh");
            var library = NativeLibrary.Load(file);
            s_start = (delegate* unmanaged[Cdecl]<byte>)NativeLibrary.GetExport(library, "sourcegit_test_host_start");
            s_settle = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(library, "sourcegit_test_host_settle");
            s_find = (delegate* unmanaged[Cdecl]<uint, Str, uint>)NativeLibrary.GetExport(library, "sourcegit_test_host_find");
            s_activate = (delegate* unmanaged[Cdecl]<uint, byte>)NativeLibrary.GetExport(library, "sourcegit_test_host_activate");
            s_close = (delegate* unmanaged[Cdecl]<uint, byte>)NativeLibrary.GetExport(library, "sourcegit_test_host_close");
            s_command = (delegate* unmanaged[Cdecl]<uint, Str, byte>)NativeLibrary.GetExport(library, "sourcegit_codeview_command");
            if (s_start() == 0)
                throw new InvalidOperationException("The headless UI didn't start");
            Application.UseThisThread();
        }

        public static void Settle() => s_settle();

        /// <summary>A control in <paramref name="window"/> by its accessible name, pressed.</summary>
        public static void Press(Window window, string name)
        {
            // What's just changed, like a button enabled, has to reach the core first.
            Settle();
            var id = WithStr(name, s => s_find(window.Id, s));
            if (id == 0)
                throw new InvalidOperationException($"Nothing named '{name}'");
            if (s_activate(id) == 0)
                throw new InvalidOperationException($"'{name}' can't be pressed");
        }

        /// <summary>Clicks a window's close button, as the user would.</summary>
        public static void Close(Window window)
        {
            if (s_close(window.Id) == 0)
                throw new InvalidOperationException("The window can't be closed");
        }

        /// <summary>An ex command in an editor, as <c>:</c> runs it.</summary>
        public static void Command(uint editor, string command)
        {
            if (WithStr(command, s => s_command(editor, s)) == 0)
                throw new InvalidOperationException($"The editor refused '{command}'");
        }

        private static T WithStr<T>(string text, Func<Str, T> f)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* p = bytes)
                return f(new Str { Ptr = p, Len = (nuint)bytes.Length });
        }
    }

    public static class Check
    {
        /// <summary>Waits for <paramref name="ready"/>, the UI turning meanwhile.</summary>
        public static async Task Until(string what, Func<bool> ready, int seconds = 20)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!ready())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Timed out waiting for {what}");
                await Task.Delay(2);
            }
        }

        public static void That(bool holds, string what)
        {
            if (!holds)
                throw new InvalidOperationException(what);
        }

        public static void Equal<T>(T want, T got, string what = "value")
        {
            if (!Equals(want, got))
                throw new InvalidOperationException($"{what}: wanted '{want}', got '{got}'");
        }

        public static void Has(string text, string part)
        {
            if (!text.Contains(part, StringComparison.Ordinal))
                throw new InvalidOperationException($"No '{part}' in:\n{text}");
        }

        public static void HasNot(string text, string part)
        {
            if (text.Contains(part, StringComparison.Ordinal))
                throw new InvalidOperationException($"'{part}' shouldn't be in:\n{text}");
        }
    }
}

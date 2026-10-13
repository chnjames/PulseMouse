using System.Runtime.InteropServices;
using PulseMouse.Core;

namespace PulseMouse.Actions;

/// <summary>Types a shortcut into whatever window has focus.</summary>
public static class KeySender
{
    private const uint KeyUp = 0x0002, ExtendedKey = 0x0001;

    public static void Send(KeyChord chord)
    {
        var keys = chord.Modifiers.Append(chord.Key).ToList();
        var inputs = new List<Input>();
        foreach (var vk in keys) inputs.Add(Key(vk, false));
        foreach (var vk in Enumerable.Reverse(keys)) inputs.Add(Key(vk, true));
        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count) throw new InvalidOperationException("发送快捷键失败：" + chord);
    }

    private static Input Key(ushort vk, bool up)
    {
        bool extended = vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or >= 0xAD and <= 0xB3;
        var input = new Input { Type = 1 };
        input.Union.Keyboard = new KeyboardInput
        {
            Vk = vk,
            Scan = (ushort)MapVirtualKey(vk, 0),
            Flags = (up ? KeyUp : 0) | (extended ? ExtendedKey : 0),
        };
        return input;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nuint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public nuint ExtraInfo; }

    // INPUT holds a union whose largest member is MOUSEINPUT; SendInput rejects a size that does not match.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
}

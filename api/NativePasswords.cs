using System.Runtime.InteropServices;

internal static class NativePasswords
{
    [DllImport("security_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr security_hash([MarshalAs(UnmanagedType.LPUTF8Str)] string password);

    [DllImport("security_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern int security_verify([MarshalAs(UnmanagedType.LPUTF8Str)] string password,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string hash);

    [DllImport("security_core", CallingConvention = CallingConvention.Cdecl)]
    private static extern void security_free(IntPtr value);

    public static string Hash(string password)
    {
        var pointer = security_hash(password);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Falha ao gerar hash.");
        try { return Marshal.PtrToStringUTF8(pointer)!; }
        finally { security_free(pointer); }
    }

    public static bool Verify(string password, string hash) => security_verify(password, hash) == 1;
}

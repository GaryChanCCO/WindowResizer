namespace System.Runtime.InteropServices.Custom
{
    internal class DangerousSafeHandle : SafeHandle
    {
        public DangerousSafeHandle(IntPtr handle) : base(handle, false)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => true;
    }
}

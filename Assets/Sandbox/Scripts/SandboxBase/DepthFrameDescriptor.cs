namespace ARSandbox
{
    // Project-owned descriptor for a depth frame's dimensions. Decouples the
    // rest of the sandbox from Windows.Kinect.FrameDescription so the same
    // code path works under the Linux Kinect driver (see CLAUDE.md / port plan).
    public struct DepthFrameDescriptor
    {
        public int Width;
        public int Height;
        public int LengthInPixels;

        public DepthFrameDescriptor(int width, int height)
        {
            Width = width;
            Height = height;
            LengthInPixels = width * height;
        }
    }
}

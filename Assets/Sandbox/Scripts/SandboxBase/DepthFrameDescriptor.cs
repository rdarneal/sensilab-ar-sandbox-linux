namespace ARSandbox
{
    // Project-owned descriptor for a depth frame's dimensions plus the IR camera
    // intrinsics libfreenect2 exposes after the depth stream has started.
    // Decouples the rest of the sandbox from Windows.Kinect.FrameDescription so
    // the same code path works under the Linux libfreenect2 driver.
    public struct DepthFrameDescriptor
    {
        public int Width;
        public int Height;
        public int LengthInPixels;
        public float MinDepthMm;
        public float MaxDepthMm;
        public float Fx;
        public float Fy;
        public float Cx;
        public float Cy;

        public DepthFrameDescriptor(int width, int height)
        {
            Width = width;
            Height = height;
            LengthInPixels = width * height;
            MinDepthMm = 0f;
            MaxDepthMm = 0f;
            Fx = 0f;
            Fy = 0f;
            Cx = 0f;
            Cy = 0f;
        }

        public DepthFrameDescriptor(int width, int height,
                                    float minDepthMm, float maxDepthMm,
                                    float fx, float fy, float cx, float cy)
        {
            Width = width;
            Height = height;
            LengthInPixels = width * height;
            MinDepthMm = minDepthMm;
            MaxDepthMm = maxDepthMm;
            Fx = fx;
            Fy = fy;
            Cx = cx;
            Cy = cy;
        }
    }
}

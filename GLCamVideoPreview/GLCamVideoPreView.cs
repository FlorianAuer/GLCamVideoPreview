using System;
using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;
using GLKit;
using OpenGLES;
using UIKit;

namespace GLCamVideoPreview
{
    public class GLCamVideoPreView : UIView
    {
        GLKView _glView;
        EAGLContext _glContext;
        CIContext _ciContext;
        AVCaptureSession _cameraSession;

        public GLCamVideoPreView(CGRect frame) : base(frame)
        {
            init();
        }

        void init()
        {
            _glContext = new EAGLContext(EAGLRenderingAPI.OpenGLES3);
            _ciContext = CIContext.FromContext(_glContext);
            _glView = MakeGLKView();

            if (EAGLContext.CurrentContext == _glContext)
                EAGLContext.SetCurrentContext(null);

            SetupAVCapture();

            this.AddSubview(_glView);
            _cameraSession.StartRunning();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _cameraSession.StopRunning();
            _cameraSession.Dispose();

            if (EAGLContext.CurrentContext == _glContext)
                EAGLContext.SetCurrentContext(null);
        }

        GLKView MakeGLKView()
        {
            var view = new GLKView
            {
                Frame = new CGRect
                {
                    X = 0,
                    Y = 0,
                    Width = this.Bounds.Width,
                    Height = this.Bounds.Height
                },
                Context = _glContext
            };

            view.BindDrawable();
            return view;
        }

        void SetupAVCapture()
        {
            _cameraSession = new AVCaptureSession();

            _cameraSession.BeginConfiguration();
            _cameraSession.SessionPreset = AVCaptureSession.Preset1920x1080;
            var captureDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) as AVCaptureDevice;

            NSError error;
            var deviceInput = new AVCaptureDeviceInput(captureDevice, out error);

            if (error != null)
            {
                Console.WriteLine("Error creating video capture device");
                return;
            }

            if (_cameraSession.CanAddInput(deviceInput))
            {
                _cameraSession.AddInput(deviceInput);
            }

            var dataOutput = new AVCaptureVideoDataOutput
            {
                WeakVideoSettings = new CVPixelBufferAttributes
                { PixelFormatType = CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange }.Dictionary,
                AlwaysDiscardsLateVideoFrames = true
            };

            var queue = new DispatchQueue("com.auerflorian.videoQueue", false);
            var dataOutputDelegate = new DataOutputDelegate(Frame, _glContext, _glView);

            dataOutput.SetSampleBufferDelegateQueue(dataOutputDelegate, queue);

            if (_cameraSession.CanAddOutput(dataOutput))
            {
                _cameraSession.AddOutput(dataOutput);
            }

            _cameraSession.CommitConfiguration();
        }
    }

    class DataOutputDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        readonly CGRect _cGRect;
        readonly EAGLContext _glContext;
        readonly GLKView _glView;
        readonly CIContext _ciContext;

        CIAffineTransform affineTransform;
        CIImage transformImage;

        int count = 0;


        public DataOutputDelegate(CGRect cgRect, EAGLContext glContext, GLKView glKView)
        {
            _cGRect = cgRect;
            _glContext = glContext;
            _glView = glKView;
            _ciContext = CIContext.FromContext(glContext);
        }

        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            //You should not call base in this method
            //base.DidOutputSampleBuffer(captureOutput, sampleBuffer, connection); 

            try
            {
                using (var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer)
                using (var image = new CIImage(pixelBuffer))
                {
                    // Rotate image 90 degree to right
                    CGAffineTransform tx;
                    tx = CGAffineTransform.MakeTranslation(image.Extent.Width / 2, image.Extent.Height / 2);
                    tx = CGAffineTransform.Rotate(tx, -1.57079f);
                    tx = CGAffineTransform.Translate(tx, -image.Extent.Width / 2, -image.Extent.Height / 2);

                    affineTransform = new CIAffineTransform()
                    {
                        Image = image,
                        Transform = tx
                    };
                    transformImage = affineTransform.OutputImage;

                    var scale = 1f;
                    InvokeOnMainThread(() => scale = (float)UIScreen.MainScreen.Scale);
                    var newFrame = new CGRect(0, 0, _cGRect.Width * scale, _cGRect.Height * scale);

                    if (_glContext != EAGLContext.CurrentContext)
                    {
                        EAGLContext.SetCurrentContext(_glContext);
                        Console.WriteLine("EaglSetCurrentContext");
                    }

                    _glView.BindDrawable();
                    _ciContext.DrawImage(transformImage, newFrame, transformImage.Extent);
                    _glView.Display();

                    CleanUp();

                }
                Console.WriteLine($"Count is {++count}");
            }
            finally
            {
                sampleBuffer.Dispose();
            }
        }

        void CleanUp()
        {
            affineTransform.Dispose();
            affineTransform = null;
            transformImage.Dispose();
            transformImage = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Forms;


using SlimDX;
using SlimDX.Direct3D11;
using VVVV.Core;
using VVVV.Utils;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V2;
using VVVV.PluginInterfaces.V1;

using FeralTic.DX11;
using FeralTic.DX11.Resources;

using Thorlabs.TSI.ColorInterfaces;
using Thorlabs.TSI.ColorProcessor;
using Thorlabs.TSI.Core;
using Thorlabs.TSI.CoreInterfaces;
using Thorlabs.TSI.Demosaicker;
using Thorlabs.TSI.ImageData;
using Thorlabs.TSI.ImageDataInterfaces;
using Thorlabs.TSI.TLCamera;
using Thorlabs.TSI.TLCameraInterfaces;

namespace VVVV.DX11.Nodes
{
    [PluginInfo(Name = "ThorCompactCam", Category = "DX11.Texture", Version = "ThorLabs", Author = "L.Rinke", Help = "Thorlabs Compact USB Camera Input")]
    public class ThorCompactCam : IPluginEvaluate, IDX11ResourceHost, IDisposable
    {

        private Bitmap _latestDisplayBitmap;
        private ITLCameraSDK _tlCameraSDK;
        private ITLCamera _tlCamera;

        private ushort[] _demosaickedData = null;
        private ushort[] _processedImage = null;
        private Demosaicker _demosaicker = new Demosaicker();
        private ColorFilterArrayPhase _colorFilterArrayPhase;
        private ColorProcessor _colorProcessor = null;
        private bool _isColor = false;
        private ColorProcessorSDK _colorProcessorSDK = null;


        bool reset = false;
        private int Width = 0;
        private int Height = 0;

        private Bitmap bmp;
        private IntPtr buffer0 = IntPtr.Zero;
        private IntPtr buffer1 = IntPtr.Zero;

        public IntPtr frontBuffer { get { return this.buffer1; } }


        [Input("ExposureTime_us", DefaultValue = 50000)]
        public ISpread<uint> FInExposureTime;

        [Input("Gain", DefaultValue = 6.0)]
        public ISpread<double> FInGain;

        [Input("Blacklevel", DefaultValue = 48)]
        public ISpread<uint> FInBlackLvl;

        [Input("Reset", IsBang = true)]
        public IDiffSpread<bool> FInReset;

        [Input("Enable")]
        public IDiffSpread<bool> FInEnable;

        [Output("Texture", IsSingle = true)]
        public Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        [Output("Frame Index", IsSingle = true, Order = 10)]
        public ISpread<int> FOutFrameIndex;

        [Output("Width Out", DefaultValue = 640)]
        public ISpread<int> FOutW;

        [Output("Height Out", DefaultValue = 480)]
        public ISpread<int> FOutH;

        [Output("Connected", DefaultBoolean = false)]
        public ISpread<bool> FOutConnected;

        [Output("Error")]
        public ISpread<String> FOutError;

        [Import()]
        public Core.Logging.ILogger FLogger;

        protected uint frameindex = 0;

        protected bool FInvalidate = false;
        protected bool FRuntimeConnected = false;

        protected object m_lock = new object();

        byte[] destination, rgba;


        public void ThorCompactCamInit()
        {
            this.reset = true;
            this._tlCameraSDK = TLCameraSDK.OpenTLCameraSDK();
            var serialNumbers = this._tlCameraSDK.DiscoverAvailableCameras();

            if (serialNumbers.Count > 0)
            {
                this._tlCamera = this._tlCameraSDK.OpenCamera(serialNumbers.First(), false);

                this._tlCamera.ExposureTime_us = FInExposureTime[0];
                if (this._tlCamera.GainRange.Maximum > 0)
                {
                    var gainIndex = this._tlCamera.ConvertDecibelsToGain(FInGain[0]);
                    this._tlCamera.Gain = gainIndex;
                }
                if (this._tlCamera.BlackLevelRange.Maximum > 0)
                {
                    this._tlCamera.BlackLevel = FInBlackLvl[0];
                }

                this._isColor = this._tlCamera.CameraSensorType == CameraSensorType.Bayer;
                if (this._isColor)
                {
                    this._colorProcessorSDK = new ColorProcessorSDK();
                    this._colorFilterArrayPhase = this._tlCamera.ColorFilterArrayPhase;
                    var colorCorrectionMatrix = this._tlCamera.GetCameraColorCorrectionMatrix();
                    var whiteBalanceMatrix = this._tlCamera.GetDefaultWhiteBalanceMatrix();
                    this._colorProcessor = (ColorProcessor)this._colorProcessorSDK.CreateStandardRGBColorProcessor(whiteBalanceMatrix, colorCorrectionMatrix, (int)this._tlCamera.BitDepth);
                }

                this._tlCamera.OperationMode = OperationMode.SoftwareTriggered;

                this._tlCamera.Arm();

                this._tlCamera.IssueSoftwareTrigger();
                this.Width = this._tlCamera.ImageWidth_pixels;
                this.Height = this._tlCamera.ImageHeight_pixels;

                this.InitBuffers();
                this.FRuntimeConnected = true;
            }
            else
            {
                FOutError[0] = "No Thorlabs camera detected.";
            }
        }

        public void ThorCompactCamDestroy()
        {
            this._tlCamera.Disarm();
            this._tlCamera.Dispose();
            this._tlCameraSDK.Dispose();
            this.reset = true;
            this.FRuntimeConnected = false;
        }

        public void Reset()
        {
            this.reset = true;
            this.ThorCompactCamDestroy();
            this.ThorCompactCamInit();
        }

        [ImportingConstructor()]
        public ThorCompactCam(IPluginHost host)
        {
           
        }

        private void InitBuffers()
        {
            this.buffer0 = Marshal.AllocHGlobal(this.Width * this.Height * 4);
            this.buffer1 = Marshal.AllocHGlobal(this.Width * this.Height * 4);
            this.destination = new byte[this.Width * this.Height * 3];
            this.rgba = new byte[this.Width * this.Height * 4];
        }
        public void Evaluate(int SpreadMax)
        {

            if (this.FTextureOutput[0] == null) { this.FTextureOutput[0] = new DX11Resource<DX11DynamicTexture2D>(); }
            try
            {
                FOutW[0] = this.bmp.Width;
                FOutH[0] = this.bmp.Height;
            }
            catch
            {

            }
            if (FInEnable.IsChanged)
            {
                if (FInEnable[0] && !this.FRuntimeConnected) {
                    ThorCompactCamInit();
                }
                else {
                    ThorCompactCamDestroy();
                }
            }

            if (FInReset.IsChanged)
            {
                if (FInReset[0])
                {
                    Reset();
                }
            }
             
            this.GetThorImage();

            FOutConnected[0] = this.FRuntimeConnected;


        }

        public void GetThorImage()
        {
            if (this.FRuntimeConnected && this._tlCamera != null)
            {
                
                // Check if a frame is available
                if (this._tlCamera.NumberOfQueuedFrames > 0)
                {
                    var frame = this._tlCamera.GetPendingFrameOrNull();
                    if (frame != null)
                    {



                        if (this._latestDisplayBitmap != null)
                        {
                            this._latestDisplayBitmap.Dispose();
                            this._latestDisplayBitmap = null;
                            this.bmp.Dispose();
                            this.bmp = null;
                        }

                        if (this._isColor)
                        {
                            var rawData = ((IImageDataUShort1D)frame.ImageData).ImageData_monoOrBGR;
                            var size = frame.ImageData.Width_pixels * frame.ImageData.Height_pixels * 3;


                            if ((this._demosaickedData == null) || (size != this._demosaickedData.Length))
                            {
                                this._demosaickedData = new ushort[size];
                            }
                            this._demosaicker.Demosaic(frame.ImageData.Width_pixels, frame.ImageData.Height_pixels, 0, 0, this._colorFilterArrayPhase, ColorFormat.BGRPixel, ColorSensorType.Bayer, frame.ImageData.BitDepth, rawData, this._demosaickedData);


                            if ((this._processedImage == null) || (size != this._demosaickedData.Length))
                            {
                                this._processedImage = new ushort[size];
                            }

                            ushort maxValue = (ushort)((1 << frame.ImageData.BitDepth) - 1);
                            this._colorProcessor.Transform48To48(_demosaickedData, ColorFormat.BGRPixel, 0, maxValue, 0, maxValue, 0, maxValue, 0, 0, 0, this._processedImage, ColorFormat.BGRPixel);
                            var imageData = new ImageDataUShort1D(_processedImage, frame.ImageData.Width_pixels, frame.ImageData.Height_pixels, frame.ImageData.BitDepth, ImageDataFormat.BGRPixel);
                            this._latestDisplayBitmap = imageData.ToBitmap_Format24bppRgb();
                            
                            this.bmp = _latestDisplayBitmap;



                            this.frameindex = frame.FrameNumber;
                        }
                        else
                        {
                            this._latestDisplayBitmap = ((ImageDataUShort1D)(frame.ImageData)).ToBitmap_Format24bppRgb();
                            this.bmp = _latestDisplayBitmap;
                        }

                        this.TextureFromBitmap(this.bmp);
                        this.FInvalidate = true;

                    }
                }
            }



            this.FOutFrameIndex[0] = (int)this.frameindex;
        }
        public void TextureFromBitmap(Bitmap bitmap)
        {

            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            int length = Math.Abs(bitmapData.Stride) * bitmapData.Height;
            int lengthA = bitmapData.Height * bitmapData.Width * 4;



            try
            {



                Marshal.Copy(bitmapData.Scan0, this.destination, 0, length);

                for (int i = 0; i < this.destination.Length / 3; i++)
                {
                    this.rgba[i * 4] = this.destination[i * 3];
                    this.rgba[i * 4 + 1] = this.destination[i * 3 + 1];
                    this.rgba[i * 4 + 2] = this.destination[i * 3 + 2];
                    this.rgba[i * 4 + 3] = 255;
                }




                Marshal.Copy(rgba, 0, this.buffer0, rgba.Length);

                IntPtr temp = this.buffer0;
                this.buffer0 = this.buffer1;
                this.buffer1 = temp;




            }
            finally
            {
                //Free bitmap-access resources
                bitmap.UnlockBits(bitmapData);
            }


        }

        
        public void Update(DX11RenderContext context)
        {

            if (this.reset)
            {
                lock (m_lock)
                {
                    if (this.FTextureOutput[0].Contains(context))
                    {
                        this.FTextureOutput[0].Dispose(context);
                    }


                    DX11DynamicTexture2D t = new DX11DynamicTexture2D(context, this.Width, this.Height, SlimDX.DXGI.Format.B8G8R8A8_UNorm);
                    this.FTextureOutput[0][context] = t;
                    this.reset = false;
                }
            }
            if (this.FInvalidate)
            {
                lock (m_lock)
                {
                    DX11DynamicTexture2D t = this.FTextureOutput[0][context];
                    if (this.Width * 4 == t.GetRowPitch())
                    {
                        t.WriteData(this.buffer1, this.Width * this.Height * 4);
                    }
                    else
                    {
                        t.WriteDataPitch(this.buffer1, this.Width * this.Height * 4);
                    }
                 
                }
                this.FInvalidate = false;
            }
        }



        public void Destroy(DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);
        }

        public void Dispose()
        {
            this.FTextureOutput[0].Dispose();

        }

        
    }



    #region PluginInfo
    [PluginInfo(Name = "DynamicBitmapTexture", Category = "DX11.Texture", Help = "Basic template with one value in/out", Tags = "")]
    #endregion PluginInfo
    public unsafe class TextureNode : IPluginEvaluate, IDX11ResourceHost, IDisposable
    {

        [Input("Reset", IsBang = true)]
        public IDiffSpread<bool> FInReset;

        [Input("Enabled", DefaultBoolean = false)]
        public IDiffSpread<bool> FInEnabled;

        [Input("Mode", DefaultValue = 0)]
        public ISpread<int> FInMode;

        [Output("Texture Out", IsSingle = true)]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        [Output("Width Out", DefaultValue = 640)]
        public ISpread<int> FOutW;

        [Output("Height Out", DefaultValue = 480)]
        public ISpread<int> FOutH;

        [Import()]
        public Core.Logging.ILogger FLogger;


        //bool invalidate = false;
        bool reset = true;
        private bool invalidate = false;
        protected object m_lock = new object();
        private Bitmap bmp;

        private IntPtr buffer0 = IntPtr.Zero;
        private IntPtr buffer1 = IntPtr.Zero;

        byte[] destination;
        byte[] rgba;

        public IntPtr frontBuffer { get { return this.buffer1; } }

        [ImportingConstructor()]
        public TextureNode(IPluginHost host) { 
        
            this.bmp = new Bitmap("c:/Users/laure/Desktop/testbild.bmp");
            this.buffer0 = Marshal.AllocCoTaskMem(this.bmp.Width * this.bmp.Height * 4);
            this.buffer1 = Marshal.AllocCoTaskMem(this.bmp.Width * this.bmp.Height * 4);
            this.destination = new byte[this.bmp.Width * this.bmp.Height * 3];
            this.rgba = new byte[this.bmp.Width * this.bmp.Height * 4];

            this.invalidate = false;
            
           
        }



            public void Evaluate(int SpreadMax)
        {
            if(FInReset[0]) {
                this.reset = true;
            }

            if (FInEnabled[0])
            {
                
                this.TextureFromBitmap(this.bmp);
                this.invalidate = true;

            }
            if (this.FTextureOutput[0] == null)
            {
                this.FTextureOutput[0] = new DX11Resource<DX11DynamicTexture2D>();
            }
            try
            {
                this.FOutW[0] = this.bmp.Width;
                this.FOutH[0] = this.bmp.Height;
            }
            catch
            {

            }


        }



        
        public void Update(DX11RenderContext context)
        {
            if (this.reset|| !this.FTextureOutput[0].Contains(context))
            {
                if (this.FTextureOutput[0].Contains(context))
                {
                    this.FTextureOutput[0].Dispose(context);
                }


                DX11DynamicTexture2D t = new DX11DynamicTexture2D(context, this.bmp.Width, this.bmp.Height, SlimDX.DXGI.Format.B8G8R8A8_UNorm);
                this.FTextureOutput[0][context] = t;
                this.reset = false;
            }


            if (this.invalidate) {

                lock (m_lock)
                {
                    DX11DynamicTexture2D t = this.FTextureOutput[0][context];
                    if (this.bmp.Width * 4 == t.GetRowPitch())
                    {
                        t.WriteData(this.buffer1,this.bmp.Width * this.bmp.Height * 4);
                    }
                    else
                    {
                        t.WriteDataPitch(this.buffer1, this.bmp.Width * this.bmp.Height * 4);
                    }
                   
                    //this.FTextureOutput[0][context].WriteData(ImageToByte(this.bmp));
                }
            }
        }

        public void Destroy(DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);
        }

        public void Dispose()
        {
            this.FTextureOutput[0].Dispose();

        }

        
        public void TextureFromBitmap(Bitmap bitmap)
        {
           
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            int length = Math.Abs(bitmapData.Stride) * bitmapData.Height;
            int lengthA = bitmapData.Height * bitmapData.Width * 4;



            try
            {



                Marshal.Copy(bitmapData.Scan0, this.destination, 0, length);
                
                for (int i = 0; i < this.destination.Length / 3; i++)
                {
                    this.rgba[i * 4] = this.destination[i * 3];
                    this.rgba[i * 4 + 1] = this.destination[i * 3 + 1];
                    this.rgba[i * 4 + 2] = this.destination[i * 3 + 2];
                    this.rgba[i * 4 + 3] = 255;
                }




                Marshal.Copy(rgba, 0, this.buffer0, rgba.Length);

                IntPtr temp = this.buffer0;
                this.buffer0 = this.buffer1;
                this.buffer1 = temp;

                
                

            }
            finally
            {
                //Free bitmap-access resources
                bitmap.UnlockBits(bitmapData);
            }


        }


    }

}

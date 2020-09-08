using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.ComponentModel.Composition;
using System.Threading;

using SlimDX;
using SlimDX.Direct3D11;
using VVVV.Core;
using VVVV.Utils;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V2;
using VVVV.PluginInterfaces.V1;
using FeralTic.DX11;
using FeralTic.DX11.Resources;


namespace VVVV.DX11.Nodes
{

   
    #region PluginInfo
    [PluginInfo(Name = "DynamicBitmapTexture", Category = "DX11.Texture", Help = "Basic template with one value in/out", Tags = "")]
	#endregion PluginInfo
    public unsafe class TextureNode : IPluginEvaluate, IDX11ResourceHost, IDisposable
    {

        [Input("Reset", Order = 505, IsBang=true)]
        IDiffSpread<bool> FInReset;

        [Input("Enabled", Order = 501, MinValue = 0)]
        IDiffSpread<bool> FInEnabled;

        [Output("Texture Out", IsSingle = true)]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        [Output("Width Out", DefaultValue = 640)]
        ISpread<int> FOutW;

        [Output("Height Out", DefaultValue = 480)]
        ISpread<int> FOutH;

        

        //bool invalidate = false;
        bool reset = false;
    	
    	private Bitmap bmp = new Bitmap("c:/splash.bmp");
    	
    	private IntPtr rgbbuffer = IntPtr.Zero;
        private IntPtr buffer0 = IntPtr.Zero;
        private IntPtr buffer1 = IntPtr.Zero;
    	
    	public IntPtr frontBuffer { get { return this.buffer1; } }

        public void Evaluate(int SpreadMax)
        {

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
    	
    	void start()
    	{
    		this.rgbbuffer = Marshal.AllocCoTaskMem(this.bmp.Width * this.bmp.Height * 3);
            this.buffer0 = Marshal.AllocCoTaskMem(this.bmp.Width * this.bmp.Height * 4);
            this.buffer1 = Marshal.AllocCoTaskMem(this.bmp.Width * this.bmp.Height * 4);
            //this.size = this.bitmap.width * this.bitmap.height * 4;
    	}
    	
    	

      
        public void Update(DX11RenderContext context)
        {
            if (this.reset)
            {
                if (this.FTextureOutput[0].Contains(context))
                {
                    this.FTextureOutput[0].Dispose(context);
                }
            	

                DX11DynamicTexture2D t = new DX11DynamicTexture2D(context, this.bmp.Width, this.bmp.Height, SlimDX.DXGI.Format.B8G8R8A8_UNorm);
                this.FTextureOutput[0][context] = t;
                this.reset = false;
            	this.start();
            }

            
            	
        	this.TextureFromBitmap(this.bmp);
            this.FTextureOutput[0][context].WriteData(this.frontBuffer, this.bmp.Width*this.bmp.Height*4);
           
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


            //Lock bitmap so it can be accessed for texture loading
            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);






            try
            {



                IntPtr temp = bitmapData.Scan0;
                this.buffer0 = this.buffer1;
                this.buffer1 = temp;

                if (this.OnFrameReady != null)
                {
                    this.OnFrameReady(this, new EventArgs());
                }

            }
            finally
            {
                //Free bitmap-access resources
                //dataStream.Dispose();

                bitmap.UnlockBits(bitmapData);

            }


        }
    }
}
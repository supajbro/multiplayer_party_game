param([string]$Source = "$PSScriptRoot/../public/logo/reference.png")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
public static class LogoLayers {
  static double Step(double p) {
    double t=Math.Max(0,Math.Min(1,(p+24)/48));
    return t*t*(3-2*t);
  }
  static double Weight(int x,int y,int[,] boxes,int n) {
    int l=boxes[n,0],t=boxes[n,1],r=l+boxes[n,2],b=t+boxes[n,3];
    return (l==0?1:Step(x-l))*(r==1920?1:1-Step(x-r))*
      (t==240?1:Step(y-t))*(b==820?1:1-Step(y-b));
  }
  public static void Extract(string source, string output) {
    Directory.CreateDirectory(output);
    using (var original = new Bitmap(source))
    using (var background = new Bitmap(original)) {
      if (original.Width != 1920 || original.Height != 1080)
        throw new Exception("Expected the unscaled 1920x1080 reference.");
      // Interpolate only unobstructed navy samples above/below the artwork.
      // This removes hidden glyphs and their glow without inventing typography.
      for (int x=0; x<1920; x++) {
        var top=original.GetPixel(x,240); var bottom=original.GetPixel(x,820);
        for (int y=241; y<820; y++) {
          double t=(y-240)/580.0;
          background.SetPixel(x,y,Color.FromArgb(
            (int)Math.Round(top.R*(1-t)+bottom.R*t),
            (int)Math.Round(top.G*(1-t)+bottom.G*t),
            (int)Math.Round(top.B*(1-t)+bottom.B*t)));
        }
      }
      background.Save(Path.Combine(output,"background.png"),ImageFormat.Png);
      string[] names={"r","o-1","m","i-1-dot","i-1-stem","l","i-2-dot","i-2-stem","o-2","line-left","games","line-right"};
      int[,] boxes={{0,240,475,380},{475,240,269,380},{744,240,366,380},
        {1110,240,130,145},{1110,385,130,235},{1240,240,130,380},
        {1370,240,125,145},{1370,385,125,235},{1495,240,425,380},
        {0,620,530,200},{530,620,865,200},{1395,620,525,200}};
      double worst=0; double total=0; long count=0;
      var layers=new Bitmap[names.Length];
      for(int n=0;n<names.Length;n++) layers[n]=new Bitmap(boxes[n,2]+48,boxes[n,3]+48,PixelFormat.Format32bppArgb);
      for(int y=240;y<820;y++) for(int x=0;x<1920;x++) {
            var s=original.GetPixel(x,y); var b=background.GetPixel(x,y);
            int[] sc={s.R,s.G,s.B}; int[] bc={b.R,b.G,b.B};
            double alpha=0;
            for(int c=0;c<3;c++) {
              int d=sc[c]-bc[c];
              if(d>0) alpha=Math.Max(alpha,d/(255.0-bc[c]));
              if(d<0) alpha=Math.Max(alpha,-d/(double)bc[c]);
            }
            int a=(int)Math.Ceiling(alpha*255); int[] fg=new int[3];
            for(int c=0;c<3;c++) {
              fg[c]=a==0 ? 0 : Math.Max(0,Math.Min(255,(int)Math.Round(bc[c]+(sc[c]-bc[c])*255.0/a)));
            }
            // Overlapping feathered masks partition unity. Factor their alphas
            // back-to-front so overlapping glows composite to the source once.
            double sumLater=0; double[] composite={(double)b.R,b.G,b.B};
            int[] alphas=new int[names.Length];
            for(int n=names.Length-1;n>=0;n--) {
              double w=Weight(x,y,boxes,n);
              double denominator=1-(a/255.0)*sumLater;
              int part=denominator<=0?0:(int)Math.Round(a*w/denominator);
              part=Math.Max(0,Math.Min(255,part)); alphas[n]=part;
              if(w>0) layers[n].SetPixel(x-boxes[n,0]+24,y-boxes[n,1]+24,Color.FromArgb(part,fg[0],fg[1],fg[2]));
              sumLater+=w;
            }
            for(int n=0;n<names.Length;n++) for(int c=0;c<3;c++)
              composite[c]=(fg[c]*alphas[n]+composite[c]*(255-alphas[n]))/255.0;
            for(int c=0;c<3;c++) {
              double error=Math.Abs(composite[c]-sc[c]);
              worst=Math.Max(worst,error); total+=error; count++;
            }
      }
      for(int n=0;n<names.Length;n++) {
        layers[n].Save(Path.Combine(output,names[n]+".png"),ImageFormat.Png);
        layers[n].Dispose();
      }
      File.WriteAllText(Path.Combine(output,"extraction-check.json"),
        "{\"maxCompositeChannelError\":"+worst.ToString(System.Globalization.CultureInfo.InvariantCulture)+
        ",\"meanCompositeChannelError\":"+(total/count).ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
    }
  }
}
'@
[LogoLayers]::Extract((Resolve-Path $Source).Path, [IO.Path]::GetFullPath("$PSScriptRoot/../public/logo"))

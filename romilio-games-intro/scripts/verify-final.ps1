Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
public static class CompareLogo {
 public static string Compare(string a,string b) {
  using(var x=new Bitmap(a)) using(var y=new Bitmap(b)) {
   long different=0; int max=0;
   for(int v=0;v<x.Height;v++) for(int u=0;u<x.Width;u++) {
    var p=x.GetPixel(u,v); var q=y.GetPixel(u,v);
    int d=Math.Max(Math.Abs(p.R-q.R),Math.Max(Math.Abs(p.G-q.G),Math.Abs(p.B-q.B)));
    if(d>0) different++; max=Math.Max(max,d);
   }
   return "Differing pixels: "+different+"; maximum channel error: "+max;
  }
 }
}
"@
[CompareLogo]::Compare((Resolve-Path "$PSScriptRoot/../public/logo/reference.png").Path,(Resolve-Path "$PSScriptRoot/../out/checks/frame-119.png").Path)

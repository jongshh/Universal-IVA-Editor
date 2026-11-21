using System;
using UnityEngine;

namespace UniversalIVAEditor
{
    public static class LayoutCoordinates
    {
        public static Transform CreateGroup(Transform parent)
        {
            RootScale(parent.localScale);
            var group=new GameObject("UIVA_LayoutTransform").transform;
            group.SetParent(parent,false); return group;
        }
        public static Vector3 RootScale(Vector3 scale)
        {
            for(int i=0;i<3;i++) if(float.IsNaN(scale[i]) || float.IsInfinity(scale[i]) || Math.Abs(scale[i])<1e-8f) throw new FormatException("IVA root scale must be finite and nonzero");
            return scale;
        }
        public static Matrix4x4 Matrix(Vector3 position,Quaternion rotation,Vector3 scale)
        {
            double length=Math.Sqrt(rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w);
            if(length<1e-8 || double.IsNaN(length) || double.IsInfinity(length)) throw new FormatException("Invalid transform rotation");
            float x=(float)(rotation.x/length),y=(float)(rotation.y/length),z=(float)(rotation.z/length),w=(float)(rotation.w/length);
            return new Matrix4x4(
                new Vector4((1-2*y*y-2*z*z)*scale.x,(2*x*y+2*w*z)*scale.x,(2*x*z-2*w*y)*scale.x,0),
                new Vector4((2*x*y-2*w*z)*scale.y,(1-2*x*x-2*z*z)*scale.y,(2*y*z+2*w*x)*scale.y,0),
                new Vector4((2*x*z+2*w*y)*scale.z,(2*y*z-2*w*x)*scale.z,(1-2*x*x-2*y*y)*scale.z,0),
                new Vector4(position.x,position.y,position.z,1));
        }
        public static Matrix4x4 Matrix(ConfigNode node) => Matrix(Configuration.Vector(node,"position",Vector3.zero),Configuration.Rotation(node),Configuration.Vector(node,"scale",Vector3.one));
        public static Vector3 EyePosition(Matrix4x4 seat,Matrix4x4 body,Matrix4x4 eyeParent,Vector3 eye,Vector3 offset) => (seat*body*eyeParent).MultiplyPoint3x4(eye+offset);
        public static void CaptureSeat(ConfigNode node,Matrix4x4 local)
        {
            for(int row=0;row<4;row++) for(int col=0;col<4;col++) if(float.IsNaN(local[row,col]) || float.IsInfinity(local[row,col])) throw new FormatException("Seat hierarchy contains a non-finite transform");
            var x=new Vector3(local.m00,local.m10,local.m20);
            var y=new Vector3(local.m01,local.m11,local.m21);
            var z=new Vector3(local.m02,local.m12,local.m22);
            var scale=new Vector3(x.magnitude,y.magnitude,z.magnitude);
            if(scale.x<1e-8f || scale.y<1e-8f || scale.z<1e-8f) throw new FormatException("Seat hierarchy has a zero scale; keep the original seat configuration");
            x/=scale.x; y/=scale.y; z/=scale.z;
            if(Math.Abs(Vector3.Dot(x,y))>0.0002f || Math.Abs(Vector3.Dot(x,z))>0.0002f || Math.Abs(Vector3.Dot(y,z))>0.0002f || Vector3.Dot(Vector3.Cross(x,y),z)<0.999f)
                throw new FormatException("Seat hierarchy contains shear or reflection; keep the original seats rather than flattening their transforms");
            var position=new Vector3(local.m03,local.m13,local.m23);
            var rotation=Rotation(x,y,z);
            EditSession.SetVector(node,"position",position); EditSession.SetRotation(node,rotation); EditSession.SetVector(node,"scale",scale);
        }
        static Quaternion Rotation(Vector3 x,Vector3 y,Vector3 z)
        {
            float trace=x.x+y.y+z.z;
            if(trace>0) { float s=(float)Math.Sqrt(trace+1)*2; return new Quaternion((y.z-z.y)/s,(z.x-x.z)/s,(x.y-y.x)/s,0.25f*s); }
            if(x.x>y.y && x.x>z.z) { float s=(float)Math.Sqrt(1+x.x-y.y-z.z)*2; return new Quaternion(0.25f*s,(y.x+x.y)/s,(z.x+x.z)/s,(y.z-z.y)/s); }
            if(y.y>z.z) { float s=(float)Math.Sqrt(1+y.y-x.x-z.z)*2; return new Quaternion((y.x+x.y)/s,0.25f*s,(z.y+y.z)/s,(z.x-x.z)/s); }
            else { float s=(float)Math.Sqrt(1+z.z-x.x-y.y)*2; return new Quaternion((z.x+x.z)/s,(z.y+y.z)/s,0.25f*s,(x.y-y.x)/s); }
        }
    }
}

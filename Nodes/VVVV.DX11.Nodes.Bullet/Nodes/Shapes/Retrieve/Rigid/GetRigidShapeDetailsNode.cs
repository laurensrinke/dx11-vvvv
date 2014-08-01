﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VVVV.PluginInterfaces.V2;
using BulletSharp;
using VVVV.Internals.Bullet;
using VVVV.Utils.VMath;



namespace VVVV.Nodes.Bullet
{
	[PluginInfo(Name = "GetRigidShapeDetails", Category = "Bullet", Author = "vux")]
	public class GetRigidShapeDetailsNode : IPluginEvaluate
	{
		[Input("Shape")]
        protected ISpread<CollisionShape> FShapes;

		[Output("Type")]
        protected ISpread<BroadphaseNativeType> FType;

		[Output("Local Scaling")]
        protected ISpread<Vector3D> FScaling;

		[Output("AABB Min")]
        protected ISpread<Vector3D> FAABBMin;

		[Output("AABB Max")]
        protected ISpread<Vector3D> FAABBMax;

		[Output("Custom")]
        protected ISpread<string> FCustom;


		[Output("Has Custom Object")]
        protected ISpread<bool> FHasCustomObj;

		[Output("Custom Object")]
        protected ISpread<ICloneable> FCustomObj;

		public void Evaluate(int SpreadMax)
		{
			this.FType.SliceCount = SpreadMax;
			this.FAABBMin.SliceCount = SpreadMax;
			this.FAABBMax.SliceCount = SpreadMax;
			this.FCustom.SliceCount = SpreadMax;
			this.FHasCustomObj.SliceCount = SpreadMax;
			this.FCustomObj.SliceCount = SpreadMax;
			this.FScaling.SliceCount = SpreadMax;

			for (int i = 0; i < SpreadMax; i++)
			{
				CollisionShape shape = FShapes[i];

				ShapeCustomData sc = (ShapeCustomData)shape.UserObject;

				Vector3 min;
				Vector3 max;
				shape.GetAabb(Matrix.Identity, out min, out max);

				this.FAABBMin[i] = min.ToVVVVector();
				this.FAABBMax[i] = max.ToVVVVector();
				this.FScaling[i] = shape.LocalScaling.ToVVVVector();

				FType[i] = shape.ShapeType;
				this.FCustom[i] = sc.CustomString;
				if (sc.CustomObject != null)
				{
					this.FHasCustomObj[i] = true;
					this.FCustomObj[i] = sc.CustomObject;
				}
				else
				{
					this.FHasCustomObj[i] = false;
					this.FCustomObj[i] = null;
				}
				
			}
		}
	}
}

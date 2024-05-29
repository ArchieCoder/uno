﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.Graphics.Effects;
using Windows.UI;

namespace Windows.UI.Composition
{
	public partial class Compositor : global::System.IDisposable
	{
		private static Lazy<Compositor> _sharedCompositorLazy = new(() => new());

		public Compositor()
		{
		}

		public long TimestampInTicks => Stopwatch.GetTimestamp();

		internal static Compositor GetSharedCompositor() => _sharedCompositorLazy.Value;

		public ContainerVisual CreateContainerVisual()
			=> new ContainerVisual(this);

		public SpriteVisual CreateSpriteVisual()
			=> new SpriteVisual(this);

		public CompositionColorBrush CreateColorBrush()
			=> new CompositionColorBrush(this);

		public CompositionColorBrush CreateColorBrush(Color color)
			=> new CompositionColorBrush(this)
			{
				Color = color
			};

		public ScalarKeyFrameAnimation CreateScalarKeyFrameAnimation()
			=> new ScalarKeyFrameAnimation(this);

		public CompositionScopedBatch CreateScopedBatch(CompositionBatchTypes batchType)
			=> new CompositionScopedBatch(this, batchType);

		public ShapeVisual CreateShapeVisual()
			=> new ShapeVisual(this);

#if __SKIA__
		internal BorderVisual CreateBorderVisual()
			=> new BorderVisual(this);
#endif

		public CompositionSpriteShape CreateSpriteShape()
			=> new CompositionSpriteShape(this);

		public CompositionSpriteShape CreateSpriteShape(CompositionGeometry geometry)
			=> new CompositionSpriteShape(this, geometry);

		public CompositionPathGeometry CreatePathGeometry()
			=> new CompositionPathGeometry(this);

		public CompositionPathGeometry CreatePathGeometry(CompositionPath path)
			=> new CompositionPathGeometry(this, path);

		public CompositionEllipseGeometry CreateEllipseGeometry()
			=> new CompositionEllipseGeometry(this);

		public CompositionLineGeometry CreateLineGeometry()
			=> new CompositionLineGeometry(this);

		public CompositionRectangleGeometry CreateRectangleGeometry()
			=> new CompositionRectangleGeometry(this);

		public CompositionRoundedRectangleGeometry CreateRoundedRectangleGeometry()
			=> new CompositionRoundedRectangleGeometry(this);

		public CompositionSurfaceBrush CreateSurfaceBrush()
			=> new CompositionSurfaceBrush(this);

		public CompositionSurfaceBrush CreateSurfaceBrush(ICompositionSurface surface)
			=> new CompositionSurfaceBrush(this, surface);

		public CompositionGeometricClip CreateGeometricClip()
			=> new CompositionGeometricClip(this);

		public CompositionGeometricClip CreateGeometricClip(CompositionGeometry geometry)
			=> new CompositionGeometricClip(this) { Geometry = geometry };

		public CompositionPropertySet CreatePropertySet()
			=> new CompositionPropertySet(this);

		public InsetClip CreateInsetClip()
			=> new InsetClip(this);

		public InsetClip CreateInsetClip(float leftInset, float topInset, float rightInset, float bottomInset)
			=> new InsetClip(this)
			{
				LeftInset = leftInset,
				TopInset = topInset,
				RightInset = rightInset,
				BottomInset = bottomInset
			};

		public RectangleClip CreateRectangleClip()
			=> new RectangleClip(this);

		public RectangleClip CreateRectangleClip(float left, float top, float right, float bottom)
			=> new RectangleClip(this)
			{
				Left = left,
				Top = top,
				Right = right,
				Bottom = bottom
			};

		public RectangleClip CreateRectangleClip(
			float left,
			float top,
			float right,
			float bottom,
			Vector2 topLeftRadius,
			Vector2 topRightRadius,
			Vector2 bottomRightRadius,
			Vector2 bottomLeftRadius)
			=> new RectangleClip(this)
			{
				Left = left,
				Top = top,
				Right = right,
				Bottom = bottom,
				TopLeftRadius = topLeftRadius,
				TopRightRadius = topRightRadius,
				BottomRightRadius = bottomRightRadius,
				BottomLeftRadius = bottomLeftRadius
			};

		public CompositionLinearGradientBrush CreateLinearGradientBrush()
			=> new CompositionLinearGradientBrush(this);

		public CompositionRadialGradientBrush CreateRadialGradientBrush()
			=> new CompositionRadialGradientBrush(this);

		public CompositionColorGradientStop CreateColorGradientStop()
			=> new CompositionColorGradientStop(this);

		public CompositionColorGradientStop CreateColorGradientStop(float offset, Color color)
			=> new CompositionColorGradientStop(this)
			{
				Offset = offset,
				Color = color
			};

		public CompositionViewBox CreateViewBox()
			=> new CompositionViewBox(this);

		public RedirectVisual CreateRedirectVisual()
			=> new RedirectVisual(this);

		public RedirectVisual CreateRedirectVisual(Visual source)
			=> new RedirectVisual(this) { Source = source };

		public CompositionVisualSurface CreateVisualSurface()
			=> new CompositionVisualSurface(this);

		public CompositionMaskBrush CreateMaskBrush()
			=> new CompositionMaskBrush(this);

		public CompositionNineGridBrush CreateNineGridBrush()
			=> new CompositionNineGridBrush(this);

		public ExpressionAnimation CreateExpressionAnimation(string expression)
			=> new ExpressionAnimation(this) { Expression = expression };

		public ExpressionAnimation CreateExpressionAnimation()
			=> new ExpressionAnimation(this);

		public Vector2KeyFrameAnimation CreateVector2KeyFrameAnimation()
			=> new Vector2KeyFrameAnimation(this);

		public Vector3KeyFrameAnimation CreateVector3KeyFrameAnimation()
			=> new Vector3KeyFrameAnimation(this);

		public Vector4KeyFrameAnimation CreateVector4KeyFrameAnimation()
			=> new Vector4KeyFrameAnimation(this);

		internal void InvalidateRender(Visual visual) => InvalidateRenderPartial(visual);
		public CompositionBackdropBrush CreateBackdropBrush()
			=> new CompositionBackdropBrush(this);

		public CompositionEffectFactory CreateEffectFactory(IGraphicsEffect graphicsEffect)
			=> new CompositionEffectFactory(this, graphicsEffect);

		public CompositionEffectFactory CreateEffectFactory(IGraphicsEffect graphicsEffect, IEnumerable<string> animatableProperties)
			=> new CompositionEffectFactory(this, graphicsEffect, animatableProperties);

		partial void InvalidateRenderPartial(Visual visual);
	}
}

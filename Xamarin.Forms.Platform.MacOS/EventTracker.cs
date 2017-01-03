﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using AppKit;

namespace Xamarin.Forms.Platform.MacOS
{
	public class EventTracker : IDisposable
	{
		readonly NotifyCollectionChangedEventHandler _collectionChangedHandler;

		readonly Dictionary<IGestureRecognizer, NSGestureRecognizer> _gestureRecognizers = new Dictionary<IGestureRecognizer, NSGestureRecognizer>();

		readonly IVisualElementRenderer _renderer;
		bool _disposed;
		NSView _handler;

		double _previousScale = 1.0;
		NSGestureEvent _shouldReceive;

		public EventTracker(IVisualElementRenderer renderer)
		{
			if (renderer == null)
				throw new ArgumentNullException("renderer");

			_collectionChangedHandler = ModelGestureRecognizersOnCollectionChanged;

			_renderer = renderer;
			_renderer.ElementChanged += OnElementChanged;
		}

		ObservableCollection<IGestureRecognizer> ElementGestureRecognizers
		{
			get
			{
				var viewElement = _renderer?.Element as View;
				if (viewElement != null)
					return viewElement.GestureRecognizers as ObservableCollection<IGestureRecognizer>;
				return null;
			}
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;

			foreach (var kvp in _gestureRecognizers)
			{
				_handler.RemoveGestureRecognizer(kvp.Value);
				kvp.Value.Dispose();
			}

			_gestureRecognizers.Clear();

			if (ElementGestureRecognizers != null)
				ElementGestureRecognizers.CollectionChanged -= _collectionChangedHandler;

			_handler = null;
		}

		public void LoadEvents(NSView handler)
		{
			if (_disposed)
				throw new ObjectDisposedException(null);

			_shouldReceive = (r, t) => r.View is IVisualElementRenderer;

			_handler = handler;
			OnElementChanged(this, new VisualElementChangedEventArgs(null, _renderer.Element));
		}

		protected virtual NSGestureRecognizer GetNativeRecognizer(IGestureRecognizer recognizer)
		{
			if (recognizer == null)
				return null;

			var weakRecognizer = new WeakReference(recognizer);
			var weakEventTracker = new WeakReference(this);

			var tapRecognizer = recognizer as TapGestureRecognizer;
			if (tapRecognizer != null)
			{
				var uiRecognizer = CreateTapRecognizer(tapRecognizer.NumberOfTapsRequired, r =>
				{
					var tapGestureRecognizer = weakRecognizer.Target as TapGestureRecognizer;
					var eventTracker = weakEventTracker.Target as EventTracker;
					var view = eventTracker?._renderer?.Element as View;

					if (tapGestureRecognizer != null && view != null)
						tapGestureRecognizer.SendTapped(view);
				});
				return uiRecognizer;
			}

			var pinchRecognizer = recognizer as PinchGestureRecognizer;
			if (pinchRecognizer != null)
			{
				double startingScale = 1;
				var uiRecognizer = CreatePinchRecognizer(r =>
				{
					var pinchGestureRecognizer = weakRecognizer.Target as IPinchGestureController;
					var eventTracker = weakEventTracker.Target as EventTracker;
					var view = eventTracker?._renderer?.Element as View;

					if (pinchGestureRecognizer != null && eventTracker != null && view != null)
					{
						var oldScale = eventTracker._previousScale;
						var originPoint = r.LocationInView(r.View);
						originPoint = NSApplication.SharedApplication.KeyWindow.ContentView.ConvertPointToView(originPoint, eventTracker._renderer.NativeView);
						var scaledPoint = new Point(originPoint.X / view.Width, originPoint.Y / view.Height);

						switch (r.State)
						{
							case NSGestureRecognizerState.Began:
								pinchGestureRecognizer.SendPinchStarted(view, scaledPoint);
								startingScale = view.Scale;
								break;
							case NSGestureRecognizerState.Changed:
								var delta = 1.0;
								var dif = Math.Abs(r.Magnification - oldScale) * startingScale;
								if (oldScale < r.Magnification)
									delta = 1 + dif;
								if (oldScale > r.Magnification)
									delta = 1 - dif;

								pinchGestureRecognizer.SendPinch(view, delta, scaledPoint);
								eventTracker._previousScale = r.Magnification;
								break;
							case NSGestureRecognizerState.Cancelled:
							case NSGestureRecognizerState.Failed:
								if (pinchGestureRecognizer.IsPinching)
									pinchGestureRecognizer.SendPinchCanceled(view);
								break;
							case NSGestureRecognizerState.Ended:
								if (pinchGestureRecognizer.IsPinching)
									pinchGestureRecognizer.SendPinchEnded(view);
								eventTracker._previousScale = 1;
								break;
						}
					}
				});
				return uiRecognizer;
			}

			var panRecognizer = recognizer as PanGestureRecognizer;
			if (panRecognizer != null)
			{
				var uiRecognizer = CreatePanRecognizer(r =>
				{
					var eventTracker = weakEventTracker.Target as EventTracker;
					var view = eventTracker?._renderer?.Element as View;

					var panGestureRecognizer = weakRecognizer.Target as IPanGestureController;
					if (panGestureRecognizer != null && view != null)
					{
						switch (r.State)
						{
							case NSGestureRecognizerState.Began:
								panGestureRecognizer.SendPanStarted(view, Application.Current.PanGestureId);
								break;
							case NSGestureRecognizerState.Changed:
								var translationInView = r.TranslationInView(_handler);
								panGestureRecognizer.SendPan(view, translationInView.X, translationInView.Y, Application.Current.PanGestureId);
								break;
							case NSGestureRecognizerState.Cancelled:
							case NSGestureRecognizerState.Failed:
								panGestureRecognizer.SendPanCanceled(view, Application.Current.PanGestureId);
								Application.Current.PanGestureId++;
								break;
							case NSGestureRecognizerState.Ended:
								panGestureRecognizer.SendPanCompleted(view, Application.Current.PanGestureId);
								Application.Current.PanGestureId++;
								break;
						}
					}
				});
				return uiRecognizer;
			}

			return null;
		}

		NSPanGestureRecognizer CreatePanRecognizer(Action<NSPanGestureRecognizer> action)
		{
			var result = new NSPanGestureRecognizer(action);
			return result;
		}

		NSMagnificationGestureRecognizer CreatePinchRecognizer(Action<NSMagnificationGestureRecognizer> action)
		{
			var result = new NSMagnificationGestureRecognizer(action);
			return result;
		}

		NSClickGestureRecognizer CreateTapRecognizer(int numTaps, Action<NSClickGestureRecognizer> action)
		{
			var result = new NSClickGestureRecognizer(action);
			result.NumberOfClicksRequired = numTaps;
			return result;
		}

		void LoadRecognizers()
		{
			if (ElementGestureRecognizers == null)
				return;

			foreach (var recognizer in ElementGestureRecognizers)
			{
				if (_gestureRecognizers.ContainsKey(recognizer))
					continue;

				var nativeRecognizer = GetNativeRecognizer(recognizer);
				if (nativeRecognizer != null)
				{
					nativeRecognizer.ShouldReceiveEvent = _shouldReceive;
					_handler.AddGestureRecognizer(nativeRecognizer);

					_gestureRecognizers[recognizer] = nativeRecognizer;
				}
			}

			var toRemove = _gestureRecognizers.Keys.Where(key => !ElementGestureRecognizers.Contains(key)).ToArray();
			foreach (var gestureRecognizer in toRemove)
			{
				var uiRecognizer = _gestureRecognizers[gestureRecognizer];
				_gestureRecognizers.Remove(gestureRecognizer);

				_handler.RemoveGestureRecognizer(uiRecognizer);
				uiRecognizer.Dispose();
			}
		}

		void ModelGestureRecognizersOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
		{
			LoadRecognizers();
		}

		void OnElementChanged(object sender, VisualElementChangedEventArgs e)
		{
			if (e.OldElement != null)
			{
				// unhook
				var oldView = e.OldElement as View;
				if (oldView != null)
				{
					var oldRecognizers = (ObservableCollection<IGestureRecognizer>)oldView.GestureRecognizers;
					oldRecognizers.CollectionChanged -= _collectionChangedHandler;
				}
			}

			if (e.NewElement != null)
			{
				// hook
				if (ElementGestureRecognizers != null)
				{
					ElementGestureRecognizers.CollectionChanged += _collectionChangedHandler;
					LoadRecognizers();
				}
			}
		}
	}
}
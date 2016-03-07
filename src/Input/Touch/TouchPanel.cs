#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2015 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region Using Statements
using System;
using System.Collections.Generic;
#endregion

namespace Microsoft.Xna.Framework.Input.Touch
{
	/// <summary>
	/// Allows retrieval of information from Touch Panel device.
	/// </summary>
	public static class TouchPanel
	{
		#region Public Static Properties

		/// <summary>
		/// The window handle of the touch panel.
		/// </summary>
		public static IntPtr WindowHandle
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the display height of the touch panel.
		/// </summary>
		public static int DisplayHeight
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the display orientation of the touch panel.
		/// </summary>
		public static DisplayOrientation DisplayOrientation
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the display width of the touch panel.
		/// </summary>
		public static int DisplayWidth
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets enabled gestures.
		/// </summary>
		public static GestureType EnabledGestures
		{
			get;
			set;
		}

		/// <summary>
		/// Returns true if a touch gesture is available.
		/// </summary>
		public static bool IsGestureAvailable
		{
			get
			{
				/* Process the pending gesture events.
				 * May cause hold events.
				 */
				UpdateGestures(false);

				return GestureList.Count > 0;
			}
		}

		#endregion
		
		#region Internal Static Properties

		/// <summary>
		/// The current timestamp that we use for setting the timestamp of new TouchLocations.
		/// </summary>
		internal static TimeSpan INTERNAL_CurrentTimestamp
		{
			get;
			set;
		}
		
		/// <summary>
		/// Whether to set the touch IDs to something similar to XNA. Checks the FNA_TOUCH_IDS_XNA environment var.
		/// </summary>
		internal static bool INTERNAL_TouchIdsXNA
		{
			get {
				string val = Environment.GetEnvironmentVariable(
					"FNA_TOUCH_IDS_XNA"
				);
				return string.IsNullOrEmpty(val) || val == "1";
			}
		}

		#endregion

		#region Internal Static Variables
		
		/// <summary>
		/// Maximum distance a touch location can wiggle and
		/// not be considered to have moved.
		/// </summary>
		internal const float TapJitterTolerance = 35.0f;

		internal static readonly TimeSpan TimeRequiredForHold = TimeSpan.FromMilliseconds(1024);

		internal static readonly Queue<GestureSample> GestureList = new Queue<GestureSample>();

		internal static GameWindow INTERNAL_Window;
		
		/// <summary>
		/// The mapping between platform specific touch ids
		/// and the touch ids we assign to touch locations.
		/// </summary>
		internal static readonly Dictionary<int, int> INTERNAL_TouchIds = new Dictionary<int, int>();
		
		/// <summary>
		/// List of all currently used touch ID values in INTERNAL_TouchIds if !INTERNAL_TouchIdsXNA.
		/// </summary>
		internal static readonly List<int> INTERNAL_TouchIdsUsed = new List<int>();
		
		/// <summary>
		/// Next ID value if INTERNAL_TouchIdsXNA.
		/// </summary>
		internal static int INTERNAL_TouchIdNext = 0;
		
		#endregion
		
		#region Private Static Variables

		/// <summary>
		/// The current touch state.
		/// </summary>
		private static readonly List<TouchLocation> touchState = new List<TouchLocation>();

		/// <summary>
		/// The current gesture state.
		/// </summary>
		private static readonly List<TouchLocation> gestureState = new List<TouchLocation>();
		
		private static TouchPanelCapabilities capabilities = new TouchPanelCapabilities();
		private static TouchPanelCapabilities capabilitiesStub = new TouchPanelCapabilities();

		#endregion
		
		#region Private Gesture Recognition Variables

		/// <summary>
		/// The pinch touch locations.
		/// </summary>
		private static readonly TouchLocation[] pinchTouch = new TouchLocation[2];

		/// <summary>
		/// If true the pinch touch locations are valid and
		/// a pinch gesture has begun.
		/// </summary>
		private static bool pinchGestureStarted;

		/// <summary>
		/// Used to disable emitting of tap gestures.
		/// </summary>
		private static bool tapDisabled;

		/// <summary>
		/// Used to disable emitting of hold gestures.
		/// </summary>
		private static bool holdDisabled;

		private static TouchLocation lastTap;

		private static GestureType dragGestureStarted = GestureType.None;
		private static int dragGestureId = -1;

		#endregion

		#region Public Static Methods

		/// <summary>
		/// Returns capabilities of touch panel device.
		/// </summary>
		/// <returns><see cref="TouchPanelCapabilities"/></returns>
		public static TouchPanelCapabilities GetCapabilities()
		{
			if (!FNAPlatform.IsOnTouchPlatform())
			{
				capabilitiesStub.InitializeStub();
				return capabilitiesStub;
			}
			capabilities.Initialize();
			return capabilities;
		}

		public static TouchCollection GetState()
		{
			if (!FNAPlatform.IsOnTouchPlatform())
			{
				return TouchCollection.Empty;
			}
			
			/* Clear out touches from previous frames that were
			 * released on the same frame they were touched that
			 * haven't been seen.
			 */
			for (int i = touchState.Count - 1; i >= 0; i -= 1)
			{
				TouchLocation touch = touchState[i];

				/* If a touch was pressed and released in a
				 * previous frame and the user didn't ask about
				 * it then trash it.
				 */
				if (	touch.SameFrameReleased &&
					touch.Timestamp < INTERNAL_CurrentTimestamp &&
					touch.State == TouchLocationState.Pressed	)
				{
					touchState.RemoveAt(i);
				}
			}

			TouchCollection result = (touchState.Count > 0) ?
				new TouchCollection(touchState.ToArray()) :
				TouchCollection.Empty;
			AgeTouches(touchState);
			return result;
		}

		/// <summary>
		/// Returns the next available gesture on touch panel device.
		/// </summary>
		/// <returns><see cref="GestureSample"/></returns>
		public static GestureSample ReadGesture()
		{
			if (!FNAPlatform.IsOnTouchPlatform())
			{
				return default(GestureSample);
			}
			return GestureList.Dequeue();
		}

		#endregion

		#region Internal Static Methods
		
		internal static void AddEvent(
			int id,
			TouchLocationState state,
			Vector2 position
		) {
			AddEvent(
				id,
				state,
				position,
				0f
			);
		}

		internal static void AddEvent(
			int id,
			TouchLocationState state,
			Vector2 position,
			float pressure
		) {
			/* Different platforms return different touch identifiers
			 * based on the specifics of their implementation and the
			 * system drivers.
			 *
			 * Sometimes these ids are suitable for our use, but other
			 * times it can recycle ids or do cute things like return
			 * the same id for double tap events.
			 *
			 * We instead provide consistent ids by generating them
			 * ourselves on the press and looking them up on move
			 * and release events.
			 */
			if (state == TouchLocationState.Pressed)
			{
				if (Environment.GetEnvironmentVariable(
					"FNA_TOUCH_FORCE_MAXIMUM"
				) != "0" && INTERNAL_TouchIds.Count == FNAPlatform.GetMaximumTouchCount())
				{
					return;
				}
				int mId;
				if (!INTERNAL_TouchIdsXNA)
				{
					mId = 0;
					for (int i = 0; i < INTERNAL_TouchIdsUsed.Count; i++)
					{
						if (INTERNAL_TouchIdsUsed[i] == mId)
						{
							mId++;
							i = -1; // repeat - we're unordered!
						}
					}
					INTERNAL_TouchIdsUsed.Add(mId);
				} else {
					if (INTERNAL_TouchIds.Count == 0)
					{
						INTERNAL_TouchIdNext = (int) INTERNAL_CurrentTimestamp.TotalMilliseconds;
					}
					mId = INTERNAL_TouchIdNext++;
				}
				INTERNAL_TouchIds[id] = mId;
			}

			// Try to find the touch id.
			int touchId;
			if (!INTERNAL_TouchIds.TryGetValue(id, out touchId))
			{
				/* If we got here that means either the device is sending
				 * us bad, out of order, or old touch events.
				 * In any case, just ignore them.
				 */
				return;
			}

			/* Add the new touch event keeping the list from getting
				* too large if no one happens to be requesting the state.
				*/
			position.X *= 0 < DisplayWidth ? DisplayWidth : INTERNAL_Window.ClientBounds.Width;
			position.Y *= 0 < DisplayHeight ? DisplayHeight : INTERNAL_Window.ClientBounds.Height;
			TouchLocation evt = new TouchLocation(
				touchId,
				state,
				position,
				pressure,
				INTERNAL_CurrentTimestamp
			);

			ApplyTouch(touchState, evt);

			/* If we have gestures enabled then start to collect
				* events for those too.
				* We also have to keep tracking any touches while
				* we know about touches so we don't miss releases
				* even if gesture recognition is disabled.
				*/
			if (EnabledGestures != GestureType.None || gestureState.Count > 0)
			{
				ApplyTouch(gestureState, evt);
				if (EnabledGestures != GestureType.None)
				{
					UpdateGestures(true);
				}
				AgeTouches(gestureState);
			}

			// If this is a release unmap the hardware id.
			if (state == TouchLocationState.Released)
			{
				INTERNAL_TouchIds.Remove(id);
				if (!INTERNAL_TouchIdsXNA)
				{
					INTERNAL_TouchIdsUsed.Remove(touchId);
				}
			}
		}

		/// <summary>
		/// This will release all touch locations.  It should only be
		/// called on platforms where touch state is reset all at once.
		/// </summary>
		internal static void ReleaseAllTouches()
		{
			int mostToRemove = Math.Max(touchState.Count, gestureState.Count);
			if (mostToRemove > 0)
			{
				// Submit a new event for each non-released location.
				int skipped = 0;
				while (touchState.Count > skipped)
				{
					if (touchState[0].State == TouchLocationState.Released)
					{
						skipped++;
						continue;
					}
					ApplyTouch(
						touchState,
						new TouchLocation(
							touchState[0].Id,
							TouchLocationState.Released,
							touchState[0].Position,
							touchState[0].PressureEXT,
							INTERNAL_CurrentTimestamp
						)
					);
				}
				
				skipped = 0;
				while (gestureState.Count > 0)
				{
					if (gestureState[0].State == TouchLocationState.Released)
					{
						skipped++;
						continue;
					}
					ApplyTouch(
						gestureState,
						new TouchLocation(
							gestureState[0].Id,
							TouchLocationState.Released,
							gestureState[0].Position,
							gestureState[0].PressureEXT,
							INTERNAL_CurrentTimestamp
						)
					);
				}
			}
		}

		#endregion

		#region Private Static Methods

		private static void AgeTouches(List<TouchLocation> state)
		{
			for (int i = state.Count - 1; i >= 0; i -= 1)
			{
				TouchLocation touch = state[i];
				if (touch.State == TouchLocationState.Released)
				{
					state.RemoveAt(i);
				}
				else if (touch.State == TouchLocationState.Pressed)
				{
					touch.AgeState();
					state[i] = touch;
				}
			}
		}

		private static void ApplyTouch(List<TouchLocation> state, TouchLocation touch)
		{
			if (touch.State == TouchLocationState.Pressed)
			{
				state.Add(touch);
				return;
			}

			// Find the matching touch
			for (int i = 0; i < state.Count; i += 1)
			{
				TouchLocation existingTouch = state[i];
				if (existingTouch.Id == touch.Id)
				{
					/* If we are moving straight from Pressed to Released,
					 * that means we've never been seen, so just get rid of us.
					 */
					if (	existingTouch.State == TouchLocationState.Pressed &&
						touch.State == TouchLocationState.Released	)
					{
						state.RemoveAt(i);
					}
					else
					{
						// Otherwise, update the touch based on the new one
						existingTouch.UpdateState(touch);
						state[i] = existingTouch;
					}
					break;
				}
			}
		}

		#endregion

		#region Private Gesture Recognition Methods

		private static bool GestureIsEnabled(GestureType gestureType)
		{
			return (EnabledGestures & gestureType) != 0;
		}

		private static void UpdateGestures(bool stateChanged)
		{
			/* These are observed XNA gesture rules which we follow below.  Please
			 * add to them if a new case is found.
			 *
			 *  - Tap occurs on release.
			 *  - DoubleTap occurs on the first press after a Tap.
			 *  - Tap, Double Tap, and Hold are disabled if a drag begins or more than one finger is pressed.
			 *  - Drag occurs when one finger is down and actively moving.
			 *  - Pinch occurs if 2 or more fingers are down and at least one is moving.
			 *  - If you enter a Pinch during a drag a DragComplete is fired.
			 *  - Drags are classified as horizontal, vertical, free, or none and stay that way.
			 *  - Drags also only occur for only one finger - multitouch isn't supported.
			 */

			// First get a count of touch locations which are not in the released state.
			int heldLocations = 0;
			for (int i = 0; i < gestureState.Count; i++)
			{
				TouchLocation touch = gestureState[i];
				heldLocations += touch.State != TouchLocationState.Released ? 1 : 0;
			}

			/* As soon as we have more than one held point then
			 * tap and hold gestures are disabled until all the
			 * points are released.
			 */
			if (heldLocations > 1)
			{
				tapDisabled = true;
				holdDisabled = true;
			}

			// Process the touch locations for gestures.
			for (int i = 0; i < gestureState.Count; i++)
			{
				TouchLocation touch = gestureState[i];
				switch (touch.State)
				{
				case TouchLocationState.Pressed:
				case TouchLocationState.Moved:
				{
					/* The DoubleTap event is emitted on first press as
					 * opposed to Tap which happens on release.
					 */
					if (touch.State == TouchLocationState.Pressed && ProcessDoubleTap(touch))
					{
						break;
					}

					/* Any time more than one finger is down and pinch is
					 * enabled then we exclusively do pinch processing.
					 */
					if (GestureIsEnabled(GestureType.Pinch) && heldLocations > 1)
					{
						// Save or update the first pinch point.
						if (pinchTouch[0].State == TouchLocationState.Invalid || pinchTouch[0].Id == touch.Id)
						{
							pinchTouch[0] = touch;
						}
						// Save or update the second pinch point.
						else if (pinchTouch[1].State == TouchLocationState.Invalid || pinchTouch[1].Id == touch.Id)
						{
							pinchTouch[1] = touch;
						}

						/* NOTE: Actual pinch processing happens outside and
						 * below this loop to ensure both points are updated
						 * before gestures are emitted.
						 */
						break;
					}

					// If we're not dragging try to process a hold event.
					float dist = Vector2.Distance(
						touch.Position,
						touch.PressPosition
					);
					if (dragGestureStarted == GestureType.None && dist < TapJitterTolerance)
					{
						ProcessHold(touch);
						break;
					}

					// If the touch state has changed then do a drag gesture.
					if (stateChanged)
					{
						ProcessDrag(touch);
					}
					break;
				}
				case TouchLocationState.Released:
				{
					/* If the touch state hasn't changed then this
					 * is an old release event... skip it.
					 */
					if (!stateChanged)
					{
						break;
					}

					/* If this is one of the pinch locations then we
					 * need to fire off the complete event and stop
					 * the pinch gesture operation.
					 */
					if (	pinchGestureStarted &&
						(	touch.Id == pinchTouch[0].Id ||
							touch.Id == pinchTouch[1].Id	)	)
					{
						if (GestureIsEnabled(GestureType.PinchComplete))
						{
							GestureList.Enqueue(
								new GestureSample(
									GestureType.PinchComplete,
									touch.Timestamp,
									Vector2.Zero,
									Vector2.Zero,
									Vector2.Zero,
									Vector2.Zero
								)
							);
						}

						pinchGestureStarted = false;
						pinchTouch[0] = TouchLocation.Invalid;
						pinchTouch[1] = TouchLocation.Invalid;
						break;
					}

					/* If there are still other pressed locations then there
					 * is nothing more we can do with this release.
					 */
					if (heldLocations != 0)
					{
						break;
					}

					/* From testing XNA it seems we need a velocity
					 * of about 100 to classify this as a flick.
					 */
					float dist = Vector2.Distance(touch.Position, touch.PressPosition);
					if (	dist > TapJitterTolerance &&
						touch.Velocity.Length() > 100.0f &&
						GestureIsEnabled(GestureType.Flick)	)
					{
						GestureList.Enqueue(
							new GestureSample(
								GestureType.Flick,
								touch.Timestamp,
								Vector2.Zero,
								Vector2.Zero,
								touch.Velocity,
								Vector2.Zero
							)
						);

						/* Fall through, a drag should still happen
						 * even if a flick does.
						 */
					}

					// If a drag is active then we need to finalize it.
					if (dragGestureStarted != GestureType.None && dragGestureId == touch.Id)
					{
						if (GestureIsEnabled(GestureType.DragComplete))
						{
							GestureList.Enqueue(
								new GestureSample(
									GestureType.DragComplete,
									touch.Timestamp,
									Vector2.Zero,
									Vector2.Zero,
									Vector2.Zero,
									Vector2.Zero
								)
							);
						}

						dragGestureStarted = GestureType.None;
						dragGestureId = -1;
						break;
					}

					// If all else fails try to process it as a tap.
					ProcessTap(touch);
					break;
				}
				}
			}

			/* If the touch state hasn't changed then there is no
			 * cleanup to do and no pinch to process.
			 */
			if (!stateChanged)
			{
				return;
			}

			// If we have two pinch points then update the pinch state.
			if (	GestureIsEnabled(GestureType.Pinch) &&
				pinchTouch [0].State != TouchLocationState.Invalid &&
				pinchTouch [1].State != TouchLocationState.Invalid	)
			{
				ProcessPinch(pinchTouch);
			}
			else
			{
				// Make sure a partial pinch state is not left hanging around.
				pinchGestureStarted = false;
				pinchTouch[0] = TouchLocation.Invalid;
				pinchTouch[1] = TouchLocation.Invalid;
			}

			// If all points are released then clear some states.
			if (heldLocations == 0)
			{
				tapDisabled = false;
				holdDisabled = false;
				dragGestureStarted = GestureType.None;
				dragGestureId = -1;
			}
		}

		private static void ProcessHold(TouchLocation touch)
		{
			if (!GestureIsEnabled(GestureType.Hold) || holdDisabled)
			{
				return;
			}

			TimeSpan elapsed = INTERNAL_CurrentTimestamp - touch.PressTimestamp;
			if (elapsed < TimeRequiredForHold)
			{
				return;
			}

			holdDisabled = true;

			GestureList.Enqueue(
				new GestureSample(
					GestureType.Hold,
					touch.Timestamp,
					touch.Position,
					Vector2.Zero,
					Vector2.Zero,
					Vector2.Zero
				)
			);
		}

		private static bool ProcessDoubleTap(TouchLocation touch)
		{
			if (	!GestureIsEnabled(GestureType.DoubleTap) ||
				tapDisabled ||
				lastTap.State == TouchLocationState.Invalid	)
			{
				return false;
			}

			/* If the new tap is too far away from the last then
			 * this cannot be a double tap event.
			 */
			float dist = Vector2.Distance(touch.Position, lastTap.Position);
			if (dist > TapJitterTolerance)
			{
				return false;
			}

			/* Check that this tap happened within the standard
			 * double tap time threshold of 300 milliseconds.
			 */
			TimeSpan elapsed = touch.Timestamp - lastTap.Timestamp;
			if (elapsed.TotalMilliseconds > 300)
			{
				return false;
			}

			GestureList.Enqueue(
				new GestureSample(
					GestureType.DoubleTap,
					touch.Timestamp,
					touch.Position,
					Vector2.Zero,
					Vector2.Zero,
					Vector2.Zero
				)
			);

			// Disable taps until after the next release.
			tapDisabled = true;

			return true;
		}

		private static void ProcessTap(TouchLocation touch)
		{
			if (tapDisabled)
			{
				return;
			}

			/* If the release is too far away from the press
			 * position then this cannot be a tap event.
			 */
			float dist = Vector2.Distance(touch.PressPosition, touch.Position);
			if (dist > TapJitterTolerance)
			{
				return;
			}

			/* If we pressed and held too long then don't
			 * generate a tap event for it.
			 */
			TimeSpan elapsed = INTERNAL_CurrentTimestamp - touch.PressTimestamp;
			if (elapsed > TimeRequiredForHold)
			{
				return;
			}

			// Store the last tap for double tap processing.
			lastTap = touch;

			// Fire off the tap event immediately.
			if (GestureIsEnabled(GestureType.Tap))
			{
				GestureSample tap = new GestureSample(
					GestureType.Tap,
					touch.Timestamp,
					touch.Position,
					Vector2.Zero,
					Vector2.Zero,
					Vector2.Zero
				);
				GestureList.Enqueue(tap);
			}
		}

		private static void ProcessDrag(TouchLocation touch)
		{
			bool dragH = GestureIsEnabled(GestureType.HorizontalDrag);
			bool dragV = GestureIsEnabled(GestureType.VerticalDrag);
			bool dragF = GestureIsEnabled(GestureType.FreeDrag);

			if (!dragH && !dragV && !dragF)
			{
				return;
			}
			
			if (dragGestureId != -1 && dragGestureId != touch.Id)
			{
				return;
			}
			
			/* Make sure this is a move event and that we have
			 * a previous touch location.
			 */
			TouchLocation prevTouch;
			if (	touch.State != TouchLocationState.Moved ||
				!touch.TryGetPreviousLocation(out prevTouch)	)
			{
				return;
			}

			Vector2 delta = touch.Position - prevTouch.Position;

			// If we're free dragging then stick to it.
			if (dragGestureStarted != GestureType.FreeDrag)
			{
				bool isHorizontalDelta = Math.Abs(delta.X) > Math.Abs(delta.Y * 2.0f);
				bool isVerticalDelta = Math.Abs(delta.Y) > Math.Abs(delta.X * 2.0f);
				bool classify = dragGestureStarted == GestureType.None;

				/* Once we enter either vertical or horizontal drags
				 * we stick to it... regardless of the delta.
				 */
				if (	dragH &&
					(	(classify && isHorizontalDelta) ||
						dragGestureStarted == GestureType.HorizontalDrag	)	)
				{
					delta.Y = 0;
					dragGestureStarted = GestureType.HorizontalDrag;
				}
				else if (	dragV &&
						(	(classify && isVerticalDelta) ||
							dragGestureStarted == GestureType.VerticalDrag	)	)
				{
					delta.X = 0;
					dragGestureStarted = GestureType.VerticalDrag;
				}

				/* If the delta isn't either horizontal or vertical
				 * then it could be a free drag if not classified.
				 */
				else if (dragF && classify)
				{
					dragGestureStarted = GestureType.FreeDrag;
				}
				else
				{
					/* If we couldn't classify the drag then
					 * it is nothing... set it to complete.
					 */
					dragGestureStarted = GestureType.DragComplete;
				}
			}

			// If the drag could not be classified then no gesture.
			if (	dragGestureStarted == GestureType.None ||
				dragGestureStarted == GestureType.DragComplete	)
			{
				return;
			}

			tapDisabled = true;
			holdDisabled = true;

			GestureList.Enqueue(
				new GestureSample(
					dragGestureStarted,
					touch.Timestamp,
					touch.Position,
					Vector2.Zero,
					delta, Vector2.Zero
				)
			);
			
			dragGestureId = touch.Id;
		}

		private static void ProcessPinch(TouchLocation[] touches)
		{
			TouchLocation prevPos0;
			TouchLocation prevPos1;

			if (!touches [0].TryGetPreviousLocation(out prevPos0))
			{
				prevPos0 = touches [0];
			}

			if (!touches [1].TryGetPreviousLocation(out prevPos1))
			{
				prevPos1 = touches [1];
			}

			Vector2 delta0 = touches[0].Position - prevPos0.Position;
			Vector2 delta1 = touches[1].Position - prevPos1.Position;

			// Get the newest timestamp.
			TimeSpan timestamp = (touches[0].Timestamp > touches[1].Timestamp) ?
				touches[0].Timestamp : touches[1].Timestamp;

			// If we were already in a drag state then fire off the drag completion event.
			if (dragGestureStarted != GestureType.None)
			{
				if (GestureIsEnabled(GestureType.DragComplete))
				{
					GestureList.Enqueue(
						new GestureSample(
							GestureType.DragComplete,
							timestamp,
							Vector2.Zero,
							Vector2.Zero,
							Vector2.Zero,
							Vector2.Zero
						)
					);
				}

				dragGestureStarted = GestureType.None;
				dragGestureId = -1;
			}

			GestureList.Enqueue(
				new GestureSample(
					GestureType.Pinch,
					timestamp,
					touches[0].Position,
					touches[1].Position,
					delta0,
					delta1
				)
			);

			pinchGestureStarted = true;
			tapDisabled = true;
			holdDisabled = true;
		}

		#endregion
	}
}

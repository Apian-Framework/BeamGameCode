using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace BeamGameCode
{
	public static class BikeFactory
	{
		// Types are pretty lame, but don't mean much to the backend
		public const string NoCtrl = "none";
		public const string RemoteCtrl = "remote";
		public const string AiCtrl = "ai";
		public const string LocalPlayerCtrl = "player";

		//
		// Utility
		//

		// Bike Factory stuff

		public static Heading PickRandomHeading()
		{
			int headInt = (int)Mathf.Clamp( Mathf.Floor(Random.Range(0,(int)Heading.kCount)), 0, 3);
			// Debug.Log(string.Format("Heading: {0}", headInt));
			return (Heading)headInt;
		}

		static  Vector2 PickRandomPos( Heading head, Vector2 basePos, float radius)
		{
			Vector2 closestPos = Ground.NearestGridPoint(
						new Vector2(Random.Range(-radius, radius), Random.Range(-radius, radius)) + basePos );

			// Random fractional offset so bikes created at the same time aren't so exactly "in sync" (Issue BeamGameCode#7)
			float offsetFrac =  Random.Range(-.35f, .35f);

			// offset from center point between basPos and next point given the heading
			return  closestPos + GameConstants.UnitOffset2ForHeading(head) * (.5f + offsetFrac) * Ground.gridSize;
		}

		public static Vector2 PositionForNewBike(BeamCoreState coreState, long curTime, Heading head, Vector2 basePos, float radius)
		{
			List<IBike> otherBikes = coreState.Bikes.Values.ToList();
			float minDist = Ground.gridSize * 2;
			float closestD = -1;
			Vector2 newPos = Vector2.zero;
			int iter = 0;

			while (closestD < minDist && iter < 100)
			{
				newPos = PickRandomPos( head, basePos,  radius);

				IBike closestBike = coreState.ClosestBikeToPos(curTime, newPos);
				closestD = closestBike == null ? minDist : Vector2.Distance(closestBike.DynamicState(curTime).position, newPos);
				if ( closestD >= minDist )
				{
					// No bike in the way  - how about claimed places?
					if ( coreState.GetNearbyPlaces(newPos,minDist).Count > 0)
						closestD = -1; // Yup, there's at least 1 - keep trying
				}
				iter++;
			}
			return newPos;
		}


	}

}

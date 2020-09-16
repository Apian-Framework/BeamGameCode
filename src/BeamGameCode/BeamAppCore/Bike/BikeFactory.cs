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
		public static Vector2 PositionForNewBike(List<IBike> otherBikes, Heading head, Vector2 basePos, float radius)
		{
			float minDist = BaseBike.length * 20;
			float closestD = -1;
			Vector2 newPos = Vector2.zero;
			int iter = 0;
			while (closestD < minDist && iter < 100)
			{
				// Note that it's using the discrete "prevPosition" property
				newPos = PickRandomPos( head, basePos,  radius);
				closestD = otherBikes.Count == 0 ? minDist : otherBikes.Select( (bike) => Vector2.Distance(bike.basePosition, newPos)).Aggregate( (acc,next) => acc < next ? acc : next);
				iter++;
			}
			return newPos;
		}


	}

}

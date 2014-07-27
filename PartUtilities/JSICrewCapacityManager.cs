﻿using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace JSIPartUtilities
{
	public class JSICrewCapacityManager: PartModule
	{
		[KSPField]
		public int capacityWhenFalse = 0;
		[KSPField]
		public int capacityWhenTrue = 0;

		[KSPField (isPersistant = true)]
		public bool currentState = false;

		[KSPField]
		public bool initialState = false;

		[KSPField (isPersistant = true)]
		public bool spawned;

		private bool warningLogged = false;

		public override void OnStart (StartState state)
		{
			if (state == StartState.Editor && !spawned) {
				currentState = initialState;
			} else {
				part.CrewCapacity = currentState ? capacityWhenTrue : capacityWhenFalse;
				if (vessel.situation == Vessel.Situations.PRELAUNCH) {
					int difference = part.protoModuleCrew.Count - part.CrewCapacity;
					if (difference > 0) {
						JUtil.LogMessage (this, "Stowaways found in part {0}", part.partName);
					}
					var stowaways = new List<ProtoCrewMember> ();
					// We go through the list backwards, assuming that the 'more important' seats are first in the list of seats.
					foreach (ProtoCrewMember thatCrewmember in part.protoModuleCrew.AsEnumerable().Reverse()) {
						if (difference > 0) {
							stowaways.Add (thatCrewmember);
							difference--;
						} else {
							break;
						}
					}
					foreach (ProtoCrewMember stowaway in stowaways) {
						part.RemoveCrewmember (stowaway);
						stowaway.seat = null;
						stowaway.rosterStatus = ProtoCrewMember.RosterStatus.Available;
					}
					// And then make sure the seat flags are correct.
					AlterCrewCapacity (part);
					GameEvents.onVesselChange.Fire (FlightGlobals.ActiveVessel);
				}
			}
			spawned = true;
		}

		[KSPEvent (active = true, guiActive = false, guiActiveEditor = false)]
		public void JSISetCrewCapacity (BaseEventData data)
		{
			if (data.GetGameObject ("objectLocal") == part.gameObject) {
				currentState = data.GetBool ("state");
				SwitchState (currentState);
			}
		}

		private void SwitchState (bool state)
		{
			part.CrewCapacity = state ? capacityWhenTrue : capacityWhenFalse;
			int difference = AlterCrewCapacity (part);
			if (difference != 0) {
				JUtil.LogErrorMessage (this, "Somehow I ended up unable to correct the Crew Capacity properly. Remaining difference: {0}", difference);
			}
		}

		// I might actually want to run this OnUpdate....
		public override void OnFixedUpdate ()
		{
			int difference = AlterCrewCapacity (part);
			if (!warningLogged && difference != 0) {
				JUtil.LogErrorMessage (this, "Somehow I ended up unable to correct the Crew Capacity properly. Remaining difference: {0}", difference);
				warningLogged = true;
			}

		}

		private static int AlterCrewCapacity (Part thatPart)
		{
			// Now the fun part.
			// This dirty hack was originally suggested by ozraven, so presented here with special thanks to him.
			// In his implementation, he actually would move the internal seat modules in and out of the list of internal seats.
			// I thought of a much simpler way, however: I can mark them as taken. 
			// All the code that adds kerbals to a seat while in flight (VAB is a very much another story) actually checks for whether the seat is taken, 
			// that is, has the 'taken' field set to true. But if it is taken, the code doesn't concern itself with what's actually in the seat.
			// So it is possible for the seat to be taken up by nothing, which is what we're going to exploit.

			int difference = 0;

			// If the internal model is null, don't do anythying.
			// Internal models get created and destroyed all the time anyway, which is why this function is called regularly.
			if (thatPart.internalModel != null) {
				// We take the crew capacity the part is supposed to have,
				// subtract the number of kerbals the part actually contains,
				// and then we subtract what the game thinks about the number of seats that are free.
				difference = (thatPart.CrewCapacity - thatPart.protoModuleCrew.Count ()) - thatPart.internalModel.GetAvailableSeatCount ();
				// The result is how many seats we need to mark empty (if we have seats that should be free but aren't)
				// or how many seats we need to mark as taken (if we have seats that shouldn't be free but are.)
				if (difference != 0) {
					foreach (InternalSeat seat in thatPart.internalModel.seats) {
						// If the seat is taken and actually contains a kerbal, we don't do anything to it, because we can't really handle
						// the case of kicking multiple kerbals out of their seats at once anyway except when at launch,
						// when it's appropriate to just remove them from the vessel and make them unassigned.
						if (!(seat.taken && seat.kerbalRef != null)) {
							// If our difference value is positive, we need to add seats,
							// so we mark them, in order, as not taken -- since we just made sure there's no kerbal in it, we must've been the ones that marked it.
							if (difference > 0 && seat.taken) {
								seat.taken = false;
								difference--;
							}
							// Otherwise we need to take away seats, so we mark them taken.
							if (difference < 0 && !seat.taken) {
								seat.taken = true;
								difference++;
							}
							// If we finished rolling away the difference, we end the loop.
							if (difference == 0)
								break;
						}

					}
				}
			}
			// If by the time we're done we ended up with any remaining difference, it means something went wrong, so we return the remaining difference,
			// so that the rest of the code can log an error message without causing incessant logging spam.
			return difference;
		}

	}
}


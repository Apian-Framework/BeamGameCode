using System;
using System.Collections.Generic;

namespace GameModeMgr
{
	// ReSharper disable UnusedType.Global,ClassNeverInstantiated.Global
	public class ModeManager {

		// Operations

		protected enum ModeOp
		{
			Nop = 0,
			Switch,
			Push,
			Pop,
			Quit
		};
		protected class OpData
		{
			public readonly ModeOp NextOp;
			public readonly int NextModeId;
			public readonly object NextParam;

			public static readonly OpData DoNothing;
			public static readonly OpData DoQuit;

			public OpData(ModeOp op, int modeId, object param)
			{
				NextOp = op;
				NextModeId = modeId;
				NextParam = param;
			}

			static OpData()
			{
				DoNothing = new OpData(ModeOp.Nop, -1, null);
				DoQuit = new OpData(ModeOp.Quit, -1, null);
			}
		}

		protected class ModeData
		{
			public readonly int ModeId;
			public readonly IGameMode Mode;
			public ModeData(int mId, IGameMode m)
			{
				ModeId = mId;
				Mode = m;
			}
		}


		//
		// Lifecycle
		//

		private readonly IModeFactory _factory;
		private readonly IModalGame _gameInst;
		private readonly Stack<ModeData> _modeDataStack;
		private OpData _nextOpData;

		public ModeManager(IModeFactory factory, IModalGame gameInst = null)
		{
			_factory = factory;
			_modeDataStack = new Stack<ModeData>();
			_nextOpData = OpData.DoNothing;
			_gameInst = gameInst;
		}

		// public API
		// ReSharper disable MemberCanBePrivate.Global,UnusedMember.Global,FieldCanBeMadeReadOnly.Global
		public void Start(int startModeId, object startParam = null) {
			_nextOpData = new OpData(ModeOp.Push, startModeId, startParam);
		}

		public void Stop()
		{
			_nextOpData = OpData.DoQuit;
		}

		public void SwitchToMode( int newModeId, object param=null)
		{
			_nextOpData = new OpData(ModeOp.Switch, newModeId, param);
		}

		public void PushMode( int newModeId, object param=null)
		{
			_nextOpData = new OpData(ModeOp.Push, newModeId, param);
		}

		public void PopMode(object result=null)
		{
			_nextOpData = new OpData(ModeOp.Pop, -1, result);
		}

		public virtual bool Loop(float frameSecs)
		{
			// return false to signal quit

			// Get current op data and reset instance var
			// IGameMode orgMode = CurrentMode();
			OpData curOpData = _nextOpData;
			_nextOpData = OpData.DoNothing;

			// stop the current state and start/resume another
			switch (curOpData.NextOp)
			{
			case ModeOp.Quit:
				_Stop();
				return false; //  short circuit exit

			case  ModeOp.Switch:
				_StopCurrentMode();
				_StartMode(curOpData);
				break;

			case ModeOp.Pop:
				string prevName = CurrentMode().GetType().Name;
				_StopCurrentMode();
				_ResumeMode(prevName, curOpData.NextParam);
				break;

			case ModeOp.Push:
				_SuspendCurrentMode();
				_StartMode(curOpData);
				break;

			}

			//# Now - whatever is current, call its loop
			if (_modeDataStack.Count > 0)
				CurrentMode().Loop(frameSecs);

			// If nothing on stack - we're done
			return _modeDataStack.Count > 0;
		}

		public IGameMode CurrentMode()
		{
			return _CurrentModeData()?.Mode;
		}

		public int CurrentModeId()
		{
			return _CurrentModeData()?.ModeId ?? -1;
		}

		// ReSharper enable MemberCanBePrivate.Global,UnusedMember.Global,FieldCanBeMadeReadOnly.Global

		//
		// Internal calls
		//
		protected ModeData _CurrentModeData()
		{
			try {
				return _modeDataStack.Peek();
			} catch (InvalidOperationException) {
				return null;
			}
		}

		protected object _StopCurrentMode()
		{
			//  pop the current state from the stack and call it's end()
			// returns the result from end()
			// NOTE: nothing is currently done with the return val from end()
			object retVal = null;
			IGameMode oldMode = CurrentMode();
			if ( oldMode != null)
			{
				retVal = oldMode.End(); // should still be on the stack (for potential GetCurrentState() during pop) TODO: Is this true?
				_modeDataStack.Pop();
			}
			return retVal;
		}
		protected void _Stop()
		{
			// Unwind the stack
			while(_modeDataStack.Count > 0)
				_StopCurrentMode();
		}

		protected void _StartMode(OpData opData)
		{
			IGameMode nextMode = _factory.Create(opData.NextModeId);
			_modeDataStack.Push(new ModeData(opData.NextModeId, nextMode));
			nextMode.Setup(this, _gameInst);
			nextMode.Start(opData.NextParam);
		}

		protected void _SuspendCurrentMode()
		{
			// Pause current state before pushing a new one - leave on stack
			CurrentMode()?.Pause();
		}

		protected void _ResumeMode(string prevStateName, object resultVal)
		{
			// Resume state a top of stack, passing it
			// the result of the state that ended
			CurrentMode()?.Resume(prevStateName, resultVal);
		}
	}
}

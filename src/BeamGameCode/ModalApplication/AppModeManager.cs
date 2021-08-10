using System;
using System.Collections.Generic;

namespace ModalApplication
{
	public abstract class AppModeManager
	{

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
			public ModeOp NextOp {get; private set;}
			public int NextModeId {get; private set;}
			public object NextParam {get; private set;}

			public static readonly OpData DoNothing = new OpData(ModeOp.Nop, -1, null);
			public static readonly OpData DoQuit = new OpData(ModeOp.Quit, -1, null);

			public OpData(ModeOp op, int modeId, object param)
			{
				NextOp = op;
				NextModeId = modeId;
				NextParam = param;
			}
		}

		protected class ModeData
		{
			public int ModeId {get; private set;}
			public IAppMode Mode {get; private set;}
			public ModeData(int mId, IAppMode m)
			{
				ModeId = mId;
				Mode = m;
			}
		}

		//
		// Lifecycle
		//

		protected IAppModeFactory _factory {get; private set;}
		protected IModalApp _gameInst {get; private set;}
		protected Stack<ModeData> _modeDataStack {get; private set;}
		protected OpData _nextOpData {get; private set;}

		protected AppModeManager(IAppModeFactory factory, IModalApp gameInst = null)
		{
			_factory = factory;
			_modeDataStack = new Stack<ModeData>();
			_nextOpData = OpData.DoNothing;
			_gameInst = gameInst;
		}

		// public API
		public virtual void Start(int startModeId, object startParam = null) {
			_nextOpData = new OpData(ModeOp.Push, startModeId, startParam);
		}

		public virtual void Stop()
		{
			_nextOpData = OpData.DoQuit;
		}

		public virtual void SwitchToMode( int newModeId, object param=null)
		{
			_nextOpData = new OpData(ModeOp.Switch, newModeId, param);
		}

		public virtual void PushMode( int newModeId, object param=null)
		{
			_nextOpData = new OpData(ModeOp.Push, newModeId, param);
		}

		public virtual void PopMode(object result=null)
		{
			_nextOpData = new OpData(ModeOp.Pop, -1, result);
		}

		public IAppMode CurrentMode()
		{
			return _CurrentModeData()?.Mode;
		}

		public int CurrentModeId()
		{
			return _CurrentModeData()?.ModeId ?? -1;
		}


		//
		// Internal calls
		//
		protected void _PerformTransition()
		{
			// Executes any pending trasition opCode

			// Get current op data and reset instance var
			OpData curOpData = _nextOpData;
			_nextOpData = OpData.DoNothing;

			// stop the current state and start/resume another
			switch (curOpData.NextOp)
			{
			case ModeOp.Quit:
				_Stop();
				return; //  short circuit exit. Stack is empty after _Stop()

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

		}


		private ModeData _CurrentModeData()
		{
			try {
				return _modeDataStack.Peek();
			} catch (InvalidOperationException) {
				return null;
			}
		}

		private object _StopCurrentMode()
		{
			//  pop the current state from the stack and call it's end()
			// returns the result from end()
			// NOTE: nothing is currently done with the return val from end()
			object retVal = null;
			IAppMode oldMode = CurrentMode();
			if ( oldMode != null)
			{
				retVal = oldMode.End(); // should still be on the stack (for potential GetCurrentState() during pop) TODO: Is this true?
				_modeDataStack.Pop();
			}
			return retVal;
		}
		private void _Stop()
		{
			// Unwind the stack
			while(_modeDataStack.Count > 0)
				_StopCurrentMode();
		}

		private void _StartMode(OpData opData)
		{
			IAppMode nextMode = _factory.Create(opData.NextModeId);
			_modeDataStack.Push(new ModeData(opData.NextModeId, nextMode));
			nextMode.Setup(this, _gameInst);
			nextMode.Start(opData.NextParam);
		}

		private void _SuspendCurrentMode()
		{
			// Pause current state before pushing a new one - leave on stack
			CurrentMode()?.Pause();
		}

		private void _ResumeMode(string prevStateName, object resultVal)
		{
			// Resume state a top of stack, passing it
			// the result of the state that ended
			CurrentMode()?.Resume(prevStateName, resultVal);
		}


	}

	public class LoopModeManager : AppModeManager
	{
		// Mode manager for an iterative "looping" application.
		// Expects "Loop()" to be called frequntly, and transitions happen in that call.
		public LoopModeManager(IAppModeFactory factory, IModalApp gameInst = null) : base(factory, gameInst) {}

		public virtual bool Loop(float frameSecs)
		{
			// return false to signal quit
			_PerformTransition(); // most of the time does nothing

			// Now - whatever is current, call its loop
			((ILoopMode)CurrentMode()).Loop(frameSecs);

			// If nothing on stack - we're done
			return _modeDataStack.Count > 0;
		}
	}

	public class FsmModeManager : AppModeManager
	{
		// Mode manager for a finite-state-machine driven app.
		// Expects transitions (push, pop, switch...) to be explicitly signalled
		// by app code,
		public FsmModeManager(IAppModeFactory factory, IModalApp gameInst = null) : base(factory, gameInst) {}

		// public API
		public override void Start(int startModeId, object startParam = null) {
			base.Start(startModeId, startParam);
			_PerformTransition();
		}

		public override void Stop()
		{
			base.Stop();
			_PerformTransition();
		}

		public override void SwitchToMode( int newModeId, object param=null)
		{
			base.SwitchToMode(newModeId, param);
			_PerformTransition();
		}

		public override void PushMode( int newModeId, object param=null)
		{
			base.PushMode(newModeId, param);
			_PerformTransition();
		}

		public override void PopMode(object result=null)
		{
			base.PopMode(result);
			_PerformTransition();
		}

	}

}

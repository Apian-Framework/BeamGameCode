using System.Collections.Generic;
using System;

namespace GameModeMgr
{
	// ReSharper disable UnusedType.Global
	public interface IModeFactory
	{
		IGameMode Create(int modeId);
	}

	public class ModeFactory : IModeFactory
	{
		// ReSharper disable MemberCanBePrivate.Global,UnusedMember.Global,FieldCanBeMadeReadOnly.Global
		protected Dictionary<int, Func<IGameMode>> ModeFactories	= null;
        public IGameMode Create(int modeId)
        {
            return ModeFactories[modeId]();
        }
	};
}


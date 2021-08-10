using System.Collections.Generic;
using System;

namespace ModalApplication
{
	public interface IAppModeFactory
	{
		IAppMode Create(int modeId);
	}

	public abstract class AppModeFactory : IAppModeFactory
	{
		protected Dictionary<int, Func<IAppMode>> AppModeCtors;
        public IAppMode Create(int modeId)
        {
            return AppModeCtors[modeId]();
        }
	};
}


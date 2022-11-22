using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json;
using Moq;
using P2pNet;
using BeamGameCode;
using GameNet;
using Apian;
using ModalApplication;

namespace P2pNetBaseTests
{
    [TestFixture]
    public class ModePlayTests
    {

/*
        public class VisiModePlay : ModePlay
        {
            public int v_curState { get => _curState;}
            public int v_kFailed { get => kFailed;}
        }

        public class TestModeFactory : AppModeFactory
        {
            public const int kPlay = 0;
            public TestModeFactory()
            {
                AppModeCtors =  new Dictionary<int, Func<IAppMode>>  {
                    { kPlay, ()=> new VisiModePlay() },
                } ;
            }
        }

        public class TestApplication : ILoopingApp
        {

        }

        public BeamGameInfo CreateGameInfo(string groupType)
        {
            string netName = "BeamNetName";
            string gameName = "BeamGameName";
            string localPeerAddr = "localPeerAddr";
            P2pNetChannelInfo groupChanInfo = new P2pNetChannelInfo(null, null, 10000, 3000, 5000,     0, 0 );// Main network channel (no clock sync)

            groupChanInfo.name = "BeamGameName";
            groupChanInfo.id = $"{netName}/{gameName}";

            ApianGroupInfo groupInfo = new ApianGroupInfo(groupType, groupChanInfo, localPeerAddr, gameName);
            return new BeamGameInfo(groupInfo);
        }


        [Test]
        //[TestCase(GameSelectedEventArgs.ReturnCode.kCancel)]
        [TestCase(GameSelectedEventArgs.ReturnCode.kCreate)]
        //[TestCase(GameSelectedEventArgs.ReturnCode.kJoin)]
        public void ModePlay_OnGameSelected(GameSelectedEventArgs.ReturnCode selRetCode)
        {
            // Mock<IAppModeFactory> mockFactory = new Mock<IAppModeFactory>(MockBehavior.Strict);

            // Mock<BeamGameNet> mockBgn = new Mock<BeamGameNet>(MockBehavior.Strict);
            // mockBgn.Setup(gn => gn.AddClient(It.IsAny<IGameNetClient>() ));

            // Mock<IBeamFrontend> mockFe = new Mock<IBeamFrontend>(MockBehavior.Strict);
            // mockFe.Setup( fe => fe.SetBeamApplication(It.IsAny<IBeamApplication>() ));
            // mockFe.Setup( fe => fe.GetUserSettings() ).Returns(BeamUserSettings.CreateDefault());
            // mockFe.Setup( fe => fe.OnStartMode(It.IsAny<int>(), It.IsAny<object>() ) );
            // mockFe.Setup(fe => fe.SetAppCore(It.IsAny<IBeamAppCore>() ));
            // mockFe.Setup(fe => fe.DisplayMessage(It.IsAny<MessageSeverity>(), It.IsAny<string>() ));

            // BeamApplication appl = new BeamApplication(mockBgn.Object, mockFe.Object);

            AppModeManager modeMgr = new LoopModeManager(new TestModeFactory(), TestApplication);


            // public void OnGameSelected( GameSelectedEventArgs args)
            VisiModePlay mp = new VisiModePlay();
            mp.Setup(null, appl);
            mp.Start();

            BeamGameInfo bgi = CreateGameInfo( SinglePeerGroupManager.kGroupType);

            GameSelectedEventArgs gsCancel = new GameSelectedEventArgs(bgi, selRetCode);

            mp.OnGameSelected(gsCancel);

            Assert.That(mp.v_curState, Is.EqualTo(mp.v_kFailed) );
        }
*/
    }
}
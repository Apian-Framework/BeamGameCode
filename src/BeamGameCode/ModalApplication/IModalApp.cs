namespace ModalApplication
{
    public interface IModalApp
    {
        void Start(int initialMode);
        void End();
    }

  public interface ILoopingApp : IModalApp
    {
        bool Loop(float frameSecs);
    }


}
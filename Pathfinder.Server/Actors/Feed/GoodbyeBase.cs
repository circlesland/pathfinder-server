namespace Pathfinder.Server.Actors.Feed
{
    public abstract class GoodbyeBase
    {
        public readonly GoodbyeReason Reason;

        public GoodbyeBase(GoodbyeReason reason)
        {
            Reason = reason;
        }
    }
}
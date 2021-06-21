namespace Pathfinder.Server.Actors.Feed
{
    public abstract class HelloBase
    {
        public readonly FeedSide Side;

        public readonly FeedMode Mode;

        // public readonly int ItemsPerTransfer = 1;

        public HelloBase(FeedSide side, FeedMode mode/*, int itemsPerTransfer*/)
        {
            Side = side;
            Mode = mode;
            // ItemsPerTransfer = itemsPerTransfer;
        }
    }
}
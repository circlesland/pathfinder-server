namespace Pathfinder.Server.Actors.Feed
{
    public sealed class FactoryResult
    {
        public readonly bool Eof;
        public readonly object Payload;

        public FactoryResult(object payload)
        {
            Eof = false;
            Payload = payload;
        }

        public FactoryResult()
        {
            Eof = true;
        }
    }
}
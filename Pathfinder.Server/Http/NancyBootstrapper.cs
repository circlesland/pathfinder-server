using Nancy;
using Nancy.Bootstrapper;
//using Nancy.Gzip;
using Nancy.TinyIoc;

namespace Pathfinder.Server.Http
{
    public class NancyBootstrapper : DefaultNancyBootstrapper
    {
        protected override void ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
        {
            /*
            var throttler = new Throtthler();
            var throttlingHooks = new ThrottlingHooks(throttler);
            throttlingHooks.Initialize(pipelines);
            */

            //CORS Enable
            pipelines.AfterRequest.AddItemToEndOfPipeline((ctx) =>
            {
                ctx.Response.WithHeader("Access-Control-Allow-Origin", "*")
                            .WithHeader("Access-Control-Allow-Methods", "POST,GET,OPTIONS")
                            .WithHeader("Access-Control-Allow-Headers", "Accept, Origin, Content-type");

            });

            //pipelines.EnableGzipCompression();
        }
    }
}
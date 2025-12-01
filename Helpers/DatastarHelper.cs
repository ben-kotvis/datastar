using Microsoft.AspNetCore.Mvc;

namespace JobBoard.Helpers
{
    public static class DatastarExtensions
    {
        public static async Task SendDatastarFragment(this HttpResponse response, string fragment)
        {
            response.ContentType = "text/event-stream";
            response.Headers["Cache-Control"] = "no-cache";
            response.Headers["Connection"] = "keep-alive";

            var lines = fragment.Split('\n');
            await response.WriteAsync("event: datastar-fragment\n");
            foreach (var line in lines)
            {
                await response.WriteAsync($"data: {line}\n");
            }
            await response.WriteAsync("\n\n");
            await response.Body.FlushAsync();
        }
        
        public static async Task SendDatastarMergeFragments(this HttpResponse response, string fragment, string selector = "", string mergeMode = "morph", bool settle = true, bool useViewTransition = false)
        {
             response.ContentType = "text/event-stream";
            response.Headers["Cache-Control"] = "no-cache";
            response.Headers["Connection"] = "keep-alive";

            await response.WriteAsync("event: datastar-merge-fragments\n");
            if (!string.IsNullOrEmpty(selector)) await response.WriteAsync($"data: selector {selector}\n");
            if (mergeMode != "morph") await response.WriteAsync($"data: merge {mergeMode}\n");
            if (!settle) await response.WriteAsync("data: settle false\n");
            if (useViewTransition) await response.WriteAsync("data: useViewTransition true\n");
            
            var lines = fragment.Split('\n');
            foreach (var line in lines)
            {
                await response.WriteAsync($"data: fragment {line}\n");
            }
            await response.WriteAsync("\n\n");
            await response.Body.FlushAsync();
        }
    }
}

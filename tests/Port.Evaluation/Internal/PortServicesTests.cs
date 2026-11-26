namespace Graphiti.Port.Evaluation.Internal;

using System.Net;
using Graphiti.Internal;

public class PortServicesTests
{
    [Theory]
    [InlineData(429, ModelCallFailure.RateLimit)]
    [InlineData(500, ModelCallFailure.ServerError)]
    [InlineData(503, ModelCallFailure.ServerError)]
    [InlineData(599, ModelCallFailure.ServerError)]
    [InlineData(600, ModelCallFailure.None)]
    [InlineData(499, ModelCallFailure.None)]
    [InlineData(400, ModelCallFailure.None)]
    [InlineData(401, ModelCallFailure.None)]
    [InlineData(200, ModelCallFailure.None)]
    public void The_default_classifier_reads_the_status_of_an_http_error(int status, ModelCallFailure expected)
    {
        var error = new HttpRequestException("failed", null, (HttpStatusCode)status);

        Assert.Equal(expected, PortServices.DefaultClassifyModelFailure(error));
    }

    [Fact]
    public void The_default_classifier_gives_None_for_an_http_error_with_no_status_and_for_other_errors()
    {
        Assert.Equal(ModelCallFailure.None, PortServices.DefaultClassifyModelFailure(new HttpRequestException("no status")));
        Assert.Equal(ModelCallFailure.None, PortServices.DefaultClassifyModelFailure(new TimeoutException()));
        Assert.Equal(ModelCallFailure.None, PortServices.DefaultClassifyModelFailure(new InvalidOperationException("429")));
    }
}

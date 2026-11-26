// Ported from tests/llm_client/test_errors.py (port/RULES.md, R25).
namespace Graphiti.Port.Evaluation;

public class TestRateLimitError
{
    /// <summary>Tests for the RateLimitError class.</summary>
    [Fact]
    public void test_default_message()
    {
        // Test that the default message is set correctly.
        var error = new RateLimitException();
        Assert.Equal("Rate limit exceeded. Please try again later.", error.Message);
        Assert.Equal("Rate limit exceeded. Please try again later.", error.Message); // str(error)
    }

    [Fact]
    public void test_custom_message()
    {
        // Test that a custom message can be set.
        var customMessage = "Custom rate limit message";
        var error = new RateLimitException(customMessage);
        Assert.Equal(customMessage, error.Message);
        Assert.Equal(customMessage, error.Message); // str(error)
    }
}

public class TestRefusalError
{
    /// <summary>Tests for the RefusalError class.</summary>
    [Fact]
    public void test_message_required()
    {
        // Test that a message is required for RefusalError.
        // PORT: Python raises TypeError at run time. C# has no call without the argument, so the test checks that
        // the parameter has no default (R4).
        var parameter = Assert.Single(typeof(RefusalException).GetConstructor([typeof(string)])!.GetParameters());
        Assert.False(parameter.HasDefaultValue);
    }

    [Fact]
    public void test_message_assignment()
    {
        // Test that the message is assigned correctly.
        var message = "The LLM refused to respond to this prompt.";
        var error = new RefusalException(message: message); // Add explicit keyword argument
        Assert.Equal(message, error.Message);
        Assert.Equal(message, error.Message); // str(error)
    }
}

public class TestEmptyResponseError
{
    /// <summary>Tests for the EmptyResponseError class.</summary>
    [Fact]
    public void test_message_required()
    {
        // Test that a message is required for EmptyResponseError.
        // PORT: Python raises TypeError at run time. C# has no call without the argument (R4).
        var parameter = Assert.Single(typeof(EmptyResponseException).GetConstructor([typeof(string)])!.GetParameters());
        Assert.False(parameter.HasDefaultValue);
    }

    [Fact]
    public void test_message_assignment()
    {
        // Test that the message is assigned correctly.
        var message = "The LLM returned an empty response.";
        var error = new EmptyResponseException(message: message); // Add explicit keyword argument
        Assert.Equal(message, error.Message);
        Assert.Equal(message, error.Message); // str(error)
    }
}

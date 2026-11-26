// Ported from graphiti_core/llm_client/errors.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti;

// PORT: the constructors only pass the message to the base class, so the skeleton ports them in full (R4).

/// <summary>Exception raised when the rate limit is exceeded.</summary>
public class RateLimitException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RateLimitException"/> class.</summary>
    public RateLimitException(string message = "Rate limit exceeded. Please try again later.")
        : this(message, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RateLimitException"/> class with the exception that caused this one.</summary>
    public RateLimitException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Exception raised when the LLM refuses to generate a response.</summary>
public class RefusalException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RefusalException"/> class.</summary>
    public RefusalException(string message)
        : this(message, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RefusalException"/> class with the exception that caused this one.</summary>
    public RefusalException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Exception raised when the LLM returns an empty response.</summary>
public class EmptyResponseException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EmptyResponseException"/> class.</summary>
    public EmptyResponseException(string message)
        : this(message, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EmptyResponseException"/> class with the exception that caused this one.</summary>
    public EmptyResponseException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

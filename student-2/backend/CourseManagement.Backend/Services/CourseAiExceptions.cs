namespace CourseManagement.Backend.Services;

public sealed class GeminiConfigurationException(string message)
    : Exception(message);

public sealed class GeminiResponseException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

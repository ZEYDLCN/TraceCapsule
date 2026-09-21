namespace TraceCapsule.AspNetCore;

/// <summary>Marks a controller/action as eligible for capsule recording when
/// <see cref="TraceCapsuleOptions.RequireAttribute"/> is enabled — lets a host opt specific
/// high-value endpoints in instead of recording every request in the app.
/// <code>
/// [TraceCapsule]
/// [HttpPost("/transfer")]
/// public async Task&lt;IActionResult&gt; Transfer(...) { }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TraceCapsuleAttribute : Attribute;

namespace NotificationService.Infrastructure.Persistence;

public class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = null!;

    public string Database { get; set; } = "notifications_db";

    public string Collection { get; set; } = "notifications";
}

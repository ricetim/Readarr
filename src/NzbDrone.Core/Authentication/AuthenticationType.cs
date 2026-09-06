namespace NzbDrone.Core.Authentication
{
    public enum AuthenticationType
    {
        None = 0,

        // 1 was Basic, removed in 11.0. Existing configurations are migrated to Forms by
        // ConfigFileProvider; the value is not reused so old config files stay recognisable.
        Forms = 2,
        External = 3
    }
}

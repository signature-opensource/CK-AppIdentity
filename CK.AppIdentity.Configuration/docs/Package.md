The configuration half of the application identity model: what you write, and the immutable objects it
becomes.

An identity is a triple - DomainName, PartyName, EnvironmentName - and a list of parties the application
interacts with. Remote parties, tenant domains for multi-tenancy, and a "Local" section for what applies
to the application itself. Whether a party is a client or a server is decided by where the Address is
declared, not by a setting.

Configuration keys are inherited downward so that common settings are written once, and the whole
analysed result is immutable for the lifetime of the process. Built on CK.Configuration.

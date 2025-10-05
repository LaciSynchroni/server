# Laci Synchroni Backend
This repo contains the Laci Synchroni backend services, consisting of:
- Server: The main server containing the SignalR hub, which is the central entrypoint
- Auth: Used to issue JWTs and other auth related things
- Services: The discord bot
- Files: CDN with the mod file cache

# How-To - Local Setup
There is a ``Local`` launch configuration for each service that starts the services with the preconfigured database on the following
ports:

| Service | Port | Metrics Port |
|---------|------|--------------|
| server  | 5000 | 5050         |
| auth    | 5001 | 5051         |
| files   | 5002 | 5052         |

That means your service URL will be ``ws://localhost:5000``.

## Configuring and Launching
A simple developer setup is provided that takes care of providing you with dependencies to start the servers without
https on your local machine.

1. Start the dependencies: ``docker compose -f docker-composen.dependencies.yaml up -d``
2. For each service, copy the ``appsettings.Example.json`` to ``appsettings.Development.json``. All configs with some sort of
suffix are in .gitignore, except the example ones, so you don't accidentally commit them.
3. Launch each service by using the ``(DEV)`` preconfigured launch config

There is a launch configuration with the ``(DEV)`` suffix that is preconfigured against the redis and postgres in that
composefile. You can simply launch these configurations, and they should connect to that database.

## Bot Setup
If you need to interact with the Laci services beyond the basics, you will need to use the bot to create an account.

First create a bot [as outlined here](https://lacisynchroni.github.io/wiki/docs/hosting/tutorial-basics/bot-setup)

After that, go to ``appsettings.Development.json`` in ``services`` and configure:
- ``LaciSynchroni.DiscordBotToken`` to your token
- ``LaciSynchroni.DiscordChannelForCommands`` to your self-service channel

## Add Account
Once connected you can register an account with the self-service bot or the ``/useradd`` command if you enabled admin access.

## Plugin Config
Because local setup doesn't have a proxy, we have to add a special auth URL to make the client connect. You can add the
following snippet to your ``servers.json`` config. **Make sure the plugin is off before adding this!**

```json
{
  "Authentications": [],
  "FullPause": false,
  "SecretKeys": {
    "0": {
      "FriendlyName": "Localhost Key",
      "Key": "<your-secret-key>"
    }
  },
  "ServerName": "Localhost",
  "ServerUri": "ws://localhost:5000",
  "AuthUri": "http://localhost:5001",
  "ServerHubUri": "",
  "UseAdvancedUris": false,
  "UseOAuth2": false,
  "OAuthToken": null,
  "HttpTransportType": 1,
  "ForceWebSockets": false
}
```

## Connect
Start the plugin and open settings. Assign your character to the Localhost Key, and then you should be able to connect


# DB Connection
## PostgreSQL
You can connect to the Postgres DB with these settings:

- Host: ``localhost``
- Port: ``5432``
- Database: ``laci``
- Username: ``laci``
- Password: ``changeit``

Or via the following JDBC url: ``jdbc:postgresql://localhost:5432/laci?user=laci&password=changeit``

## Redis
- Host: ``localhost``
- Port: ``6379``
- Password: ``changeit``

## Add Admin Account
If you want to create an admin account so you can use admin features, execute this in your database. Make sure to
replace ``<discord-id>`` with your discord ID.

```sql
insert into users(uid, is_moderator, is_admin, last_logged_in) values ('admin', true, true, current_timestamp);
insert into lodestone_auth (discord_id, user_uid) values ('<discord-id>', 'admin');
```
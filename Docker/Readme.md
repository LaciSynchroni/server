# Laci Synchroni Docker Setup
This is an easy to use setup that uses [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) to run Laci Synchroni on a server without exposing that server to the internet.
You must have the following requirements met:
- Git and Docker installed on the machine you want to run at
- A Cloudflare account for Cloudflare Tunnel (it's free for non-commercial use)
- A domain

If you still need a Cloudflare Tunnel, [you can follow the instructions here](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/get-started/create-remote-tunnel/).

# Setup
The following setup assumes you are using the ``standalone`` setup. The sharded setup is for larger servers. Should you need this,
you probably already know how to adapt this guide to use ``sharded`` mode!

## 1. Checkout this repo
On your server:
1. ``git clone https://github.com/LaciSynchroni/server.git laci``
2. ``cd laci/Docker``

You now have everything to start configuring the server before booting for the first time.

## 2. Configuring environment
Assuming you are still in the ``Docker`` directory, do the following copy commands:
1. ``cp ./compose/.env.example .env``
2. ``cp ./compose/standalone.example.yml ./compose/standalone.yml``
3. ``cp ./config/standalone/authservice.appsettings.example.json ./config/standalone/authservice.appsettings.json``
4. ``cp ./config/standalone/base.appsettings.example.json ./config/standalone/base.appsettings.json``
5. ``cp ./config/standalone/files.appsettings.example.json ./config/standalone/files.appsettings.json``
6. ``cp ./config/standalone/server.appsettings.example.json ./config/standalone/server.appsettings.json``
7. ``cp ./config/standalone/services.appsettings.example.json ./config/standalone/services.appsettings.json``

**Now edit the ``.env`` file.** Make sure to fill out all required values. The comments and pre-filled values should help guide you
through it. **Please use secure passwords!**

## 3. (Optional) Removing Grafana/Prometheus
**Grafana and Prometheus provide you a dashboard with some insights, like network IO and storage usage**. They are not
needed to run Laci Synchroni, but can be useful.

You can, however, remove them if you run on a smaller server or simply don't have use for those.

You can simply remove them out of the ``standalone.yml``

## 4. Configuring Cloudflare Tunnel
In your Cloudflare Tunnel, you should configure the following under "public hostnames" in this order:

|   | Public hostname          | Path  | Service                  |
|---|--------------------------|-------|--------------------------|
| 1 | laci.<your_domain>       | auth  | http://laci-auth:6500    |
| 2 | laci.<your_domain>       | oauth | http://laci-auth:6500    |
| 3 | laci.<your_domain>       | *     | http://laci-server:6000  |
| 4 | lacicdn.<your_domain>    | *     | http://laci-files:6200   |
| 5 | lacistats.<your_domain>  | *     | http://grafana:3000      |

Naturally, you can also do the proxying with another service or on your own.

## 5. Run the Laci Synchroni Server
Start the services using either `./linux.sh` or `.\windows.ps1`.
There are two modes, which are mutually exclusive:
- `--standalone` (`-Standalone` for Windows) to run the services as a single instance.
- `--sharded` (`-Sharded` for Windows) to run the services in a sharded configuration.

By supplying `start` as a subcommand, the services will be started in the background. To stop them, you can use the `stop` subcommand.
If you do not provide either `start` or `stop`, the services will run in the foreground.

Here are a few examples:

```bash
# Start the services in standalone mode
./linux.sh --standalone start

# Start the services in sharded mode
./linux.sh --sharded start

# Stop the services
./linux.sh stop

# Start in the foreground
./linux.sh --standalone
```

```ps1
# Start the services in standalone mode
.\windows.ps1 -Standalone start

# Start the services in sharded mode
.\windows.ps1 -Sharded start

# Stop the services
.\windows.ps1 stop

# Start in the foreground
.\windows.ps1 -Standalone
```

## 6 Testing and Next Steps
Please check over on Discord on how to proceed from here on out.
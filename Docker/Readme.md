# Laci Synchroni Docker Setup
This is primarily aimed at developers who want to spin up their own local server for development purposes without having to spin up a VM, but also works for production ready servers, granted you have the knowledge to configure it securely.
Requires Docker to be installed on the machine.

## 1. Configure ports + token
Head to the `compose` directory, make a copy of the `.env.example` file named `.env`, and edit the environment variables inside of there with the appropriate values.
The compose files provided uses Cloudflare Tunnels for simplified access to the services.

In your Cloudflare Tunnel, you should configure the following under "public hostnames" in this order:

|   | Public hostname          | Path  | Service                  |
|---|--------------------------|-------|--------------------------|
| 1 | laci.<your_domain>       | auth  | http://laci-auth:6500    |
| 2 | laci.<your_domain>       | oauth | http://laci-auth:6500    |
| 3 | laci.<your_domain>       | *     | http://laci-server:6000  |
| 4 | lacicdn.<your_domain>    | *     | http://laci-files:6200   |
| 5 | lacistats.<your_domain>  | *     | http://grafana:3000      |

Naturally, you can also do the proxying with another service or on your own.

## 2. Run the Laci Synchroni Server
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

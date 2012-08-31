### A .NET Session State Store Provider that uses Memcached. 

Out of the box .NET sessions are stored on the web server in memory, which is perfect for single-server environments. Load balanced environments with multiple servers however require a little more.

The alternatives:

1. Use a database - Regardless of which database camp you're on, using it for this purpose is wxtremely inefficient and should not be deployed in any production environment.
2. L7 load balance - Not extremely, but also inefficient. Requires special hardware. Prevents continuos balancing for a single user.

[Memcached](http://memcached.org/) is an open-source distributed memory caching system that fits the bill perfectly:
- It's lightweight
- It's in-memory so it's fast
- It's distributed and very easy to scale

# Setup

### Set up a Memcached server

Duh. Many articles have been written about setting up Memcached. Use the one you like the most.

### Add BeITMemcached to your project

The Memcached client used in this project is BeITMemcached. It's lean, fast, and easy to work with. 

A compiled version (dll) is included for simplicity and can be dropped into you /bin folder. The complete source is available on the project's homepage at http://code.google.com/p/beitmemcached/. Pick the way you like better and get BeIT into your project.

### Set up a Memcached pool

Defining a pool assigns one or more memcached servers a friendly name that can be used programmatically.

Add the following to your Global.asax file. Feel free to change _DefaultPool_.

```
void Application_Start(object sender, EventArgs e) {
  BeIT.MemCached.MemcachedClient.Setup("DefaultPool", new String[] { "server:port" });
}
```

### Add the custom provider to your project

Add `MemcachedSessionStateStore.cs` and `LockInfo.cs` to your `App_Code` folder.

### Register the provider

Update the SessionState section of your Web.Config file as follows:

```
<sessionState customProvider="Memcached" mode="Custom" regenerateExpiredSessionId="true">
  <providers>
    <add name="Memcached" type="MemcachedSessionStateStore" ApplicationName="[application]" description="" pool="[pool]"/>
  </providers>
</sessionState>
```

Where:
- __[application]__ is a unique name for your application. This is used to ensure multiple applications can run on the same Memcached server.
- __[pool]__ is the name of the pool you defined in the previous step.

That's it. Your sessions should now be stored in Memcached.

# Warnings

* Memcached was/is not really meant to run on Windows. While there are ways to set it up none are recommended for production environments. __Run your Memcached on Linux!__.
* Memcached stores everything in memory which means a restart of the daemon or the server will result in the loss of all sessions. __If your application can't sustain such loss you'll need to find a more presistent place to store your sessions!__
using System;
using System.Configuration;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.SessionState;
using BeIT.MemCached;

/// <summary>
/// A .NET Session Store that uses Memcached
/// Created by Rob Blankaert
/// Copyright 2012 appFigures
/// </summary>
public class MemcachedSessionStateStore : SessionStateStoreProviderBase
{
    public String ApplicationName { set; get; }

    /// <summary>
    /// An invalidation key
    /// </summary>
    public String VersionKey = "0";

    /// <summary>
    /// Default name of memcached pool. This can (and should) be overridden in Web.Config
    /// </summary>
    private String pool = "sessions";
    
    public bool WriteExceptionsToEventLog { get; set; }
    private SessionStateSection sessionConfig = null;
    
    private MemcachedClient _client = null;
    private MemcachedClient client
    {
        get
        {
            if (_client == null) {
                _client = MemcachedClient.GetInstance(pool);
            }
            return _client;
        }
    }

    public MemcachedSessionStateStore() { }

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="name">A friednly name for the provider.</param>
    /// <param name="config">A config object for the provider.</param>
    public override void Initialize(String name, System.Collections.Specialized.NameValueCollection config)
    {
        // Initialize values from Web.Config
        if (config == null) throw new ArgumentNullException("config");

        if (name == null || name.Length == 0) name = "MemcachedSessionStateStore";

        if (String.IsNullOrEmpty(config["description"])) {
            config.Remove("description");
            config.Add("description", "BeITMemcached based Session Store");
        }

        if (!String.IsNullOrEmpty(config["pool"])) {
            pool = config["pool"];
        }

        // Initialize the abstract base class
        base.Initialize(name, config);

        // Initialize the ApplicationName property
        ApplicationName = System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath;

        // Get <sessionState> configuration element
        Configuration cfg = WebConfigurationManager.OpenWebConfiguration(ApplicationName);
        ConfigurationSection sec = cfg.GetSection("system.web/sessionState");
        sessionConfig = (SessionStateSection)sec;
    }

    /// <summary>
    /// Sets a reference to the SessionStateItemExpireCallback delegate for the Session_OnEnd event defined in the Global.asax file.
    /// </summary>
    /// <param name="expireCallback">The SessionStateItemExpireCallback delegate for the Session_OnEnd event defined in the Global.asax file.</param>
    /// <returns>true if the session-state store provider supports calling the Session_OnEnd event; otherwise, false.</returns>
    /// <remarks>This callback is not supported in this provider</remarks>
    public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
    {
        return false;
    }

    /// <summary>
    /// Updates a locked session and releases the lock.
    /// </summary>
    /// <param name="context">The current request's HttpContext.</param>
    /// <param name="id">The session id for the current request.</param>
    /// <param name="item">The SessionStateStoreData object that contains the current session values to be stored.</param>
    /// <param name="lockId">The lock id for the current request.</param>
    /// <param name="newItem">Whether this is a new item or not.</param>
    public override void SetAndReleaseItemExclusive(HttpContext context, String id, SessionStateStoreData item, object lockId, bool newItem)
    {
        // Set session
        client.Set(GetSessionHash(id), Serialize((SessionStateItemCollection)item.Items), sessionConfig.Timeout);

        // Remove lock (no longer exclusive)
        client.Delete(GetSessionLockHash(id));
    }

    /// <summary>
    /// Unlocks a session.
    /// </summary>
    /// <param name="context">The current request's HttpContext.</param>
    /// <param name="id">The session id for the current request.</param>
    /// <param name="lockId">The lock id for the current request.</param>
    public override void ReleaseItemExclusive(HttpContext context, String id, object lockId)
    {
        client.Delete(GetSessionLockHash(id));
    }

    /// <summary>
    /// Retrieves the contents of the specified session id.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="id">The session id for the current request.</param>
    /// <param name="locked">Whether the requested session is currently locked.</param>
    /// <param name="lockAge">Amount of time this session has been locked for.</param>
    /// <param name="lockId">The lock id for the current request.</param>
    /// <param name="actions"></param>
    /// <returns>The SessionStateStoreData if found, null if not.</returns>
    public override SessionStateStoreData GetItem(HttpContext context, String id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
    {
        locked = false;
        lockId = 0;
        lockAge = TimeSpan.Zero;
        actions = SessionStateActions.None;

        byte[] o = GetSessionData(id);

        if (o != null) {
            return new SessionStateStoreData(Deserialize(o), null, (int)sessionConfig.Timeout.TotalMinutes);
        } else {
            return CreateNewStoreData(context, (int)sessionConfig.Timeout.TotalMinutes);
        }
    }

    /// <summary>
    /// Retrieves the contents of the specified session id and locks it for the duration of the request.
    /// </summary>
    /// <param name="context">The current request's HttpContext.</param>
    /// <param name="id">The session id for the current request.</param>
    /// <param name="locked">Whether the requested session is currently locked.</param>
    /// <param name="lockAge">Amount of time this session has been locked for.</param>
    /// <param name="lockId">The lock id for the current request.</param>
    /// <param name="actions"></param>
    /// <returns>The SessionStateStoreData if found, null if not.</returns>
    public override SessionStateStoreData GetItemExclusive(HttpContext context, String id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
    {
        locked = false;
        lockAge = TimeSpan.Zero;
        lockId = null;
        actions = SessionStateActions.None;

        // Try to get the lock
        if (!client.Add(GetSessionLockHash(id), new LockInfo(), DateTime.Now.AddSeconds(10))) {
            LockInfo l = GetLock(id);
            if (l != null) {
                locked = true;
                lockId = l.LockID;
                lockAge = DateTime.Now.Subtract(l.LockTime);                
                return null;
            }
        }

        byte[] o = GetSessionData(id);

        if (o != null) {
            return new SessionStateStoreData(Deserialize(o), null, (int)sessionConfig.Timeout.TotalMinutes);
        } else {
            return CreateNewStoreData(context, (int)sessionConfig.Timeout.TotalMinutes);
        }
    }

    /// <summary>
    /// Deletes a session.
    /// </summary>
    /// <param name="context">The current request's HttpContext.</param>
    /// <param name="id">The session id for the current request.</param>
    /// <param name="lockId">The lock id for the current request.</param>
    /// <param name="item">The SessionStateStoreData to be removed.</param>
    /// <remarks>The SessionStateStoreData is not used in this case since we use the session id to generate the Memcached key.</remarks>
    public override void RemoveItem(HttpContext context, String id, object lockId, SessionStateStoreData item)
    {
        client.Delete(GetSessionHash(id));
        
        // Notice we're not removing the lock here even though 
        // we should, but since session ids are unique there's no reason 
        // to waste time waiting for the delete to finish.

        // If you want to delete the lock object anyway uncomment the line below
        //client.Delete(GetSessionLockHash(id));
    }

    /// <summary>
    /// Creates a new SessionStateStoreData object to be used for the current request.
    /// </summary>
    /// <param name="context">The current request's HttpContext.</param>
    /// <param name="timeout">The session's timeout.</param>
    /// <returns>A SessionStateStoreData to use for this request.</returns>
    public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
    {
        return new SessionStateStoreData(new SessionStateItemCollection(),
            SessionStateUtility.GetSessionStaticObjects(context),
            timeout);
    }

    /// <summary>
    /// Updates the expiration date and time for the given session id.
    /// </summary>
    /// <param name="context">The current request's HttpContext.</param>
    /// <param name="id">The session id for the current request.</param>
    public override void ResetItemTimeout(HttpContext context, String id)
    {
        byte[] o = GetSessionData(id);
        if (o != null) client.Set(GetSessionHash(id), o, sessionConfig.Timeout);
    }

    public override void CreateUninitializedItem(HttpContext context, String id, int timeout) { }
    public override void InitializeRequest(HttpContext context) { }
    public override void EndRequest(HttpContext context) { }
    public override void Dispose() { }

    /// <summary>
    /// Converts the session's content into a byte array so we can store it in Memcached.
    /// </summary>
    /// <param name="items">The session's items that should be stored in Memcached.</param>
    /// <returns>A byte array that can be saved in Memcached.</returns>
    private static byte[] Serialize(SessionStateItemCollection items)
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(ms);

        if (items != null) items.Serialize(writer);

        writer.Close();

        return ms.ToArray();
    }

    /// <summary>
    /// Converts a session's content from its cached representation so we can use it.
    /// </summary>
    /// <param name="serializedItems">Raw data from Memcached.</param>
    /// <returns>A SessionStateItemCollection.</returns>
    private static SessionStateItemCollection Deserialize(byte[] serializedItems)
    {
        if (serializedItems != null) {
            MemoryStream ms = new MemoryStream(serializedItems);
            if (ms.Length > 0) {
                BinaryReader reader = new BinaryReader(ms);
                return SessionStateItemCollection.Deserialize(reader);
            }
        }

        return new SessionStateItemCollection();
    }

    /// <summary>
    /// Creates a unique id for the session item.
    /// </summary>
    /// <param name="id">The session id for the current request.</param>
    /// <returns>A unique key for this session.</returns>
    private String GetSessionHash(String id)
    {
        return String.Format("SESSION:{0}:{1}:{2}",
            VersionKey,
            ApplicationName,
            id);
    }

    /// <summary>
    /// Creates a unique id for the session's lock item.
    /// </summary>
    /// <param name="id">The session id for the current request.</param>
    /// <returns>A unique key for this session's lock.</returns>
    private String GetSessionLockHash(String id)
    {
        return String.Format("SESSION:{0}:{1}:{2}:LOCK", 
            VersionKey, 
            ApplicationName, 
            id);
    }

    /// <summary>
    /// Retreive session data from Memcached.
    /// </summary>
    /// <param name="id">The session id to look up.</param>
    /// <returns>A byte array with data for the requested session id.</returns>
    private byte[] GetSessionData(String id)
    {
        Object o = client.Get(GetSessionHash(id));

        try {
            byte[] b = (byte[])o;
            return b;
        } catch (InvalidCastException) {
            return null;
        }
    }

    /// <summary>
    /// Gets a LockInfo object for the given session id.
    /// </summary>
    /// <param name="id">The id of the session to get the LockInfo for.</param>
    /// <returns>The corresponding LockInfo object, null if none exists.</returns>
    private LockInfo GetLock(String id)
    {
        return client.Get(GetSessionLockHash(id)) as LockInfo;
    }
}

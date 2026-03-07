using System.Text.Json;

namespace NovaLog.Core.Services;

/// <summary>
/// Writes realistic sample log lines into a target directory for development/testing.
/// Creates audit JSON files and rotates hourly log files, just like Winston.
/// Toggle on/off via Start()/Stop().
/// </summary>
public sealed class LogSimulator : IDisposable
{
    private readonly string _directory;
    private readonly Random _rng = new();
    private System.Threading.Timer? _timer;
    private string? _currentFile;
    private DateTime _currentHour;
    private int _lineCount;
    private bool _disposed;

    /// <summary>When true, generates all syntax types (JSON, SQL, stack traces, numbers) for testing highlighting.</summary>
    public bool ShowcaseMode { get; init; }

    // Simulated log templates
    private static readonly (string Level, string[] Messages)[] Templates =
    [
        ("debug", [
            "sendGatewayCommand method: getDevices command: None",
            "Entering checkForIdleWorkers",
            "Using auto checkout threshold of 60 minutes",
            "Retrieving all open Check In records",
            "Found {0} open Check In records to process",
            "Cache miss for rack Ready for Milling ({0}) - running query",
            "Cached rankings for rack Assembly ({0}) with {1} positions - invalidation flag cleared",
            "Memory usage (periodic) - RSS: {0} MB, Heap Used: {1} MB, Heap Total: {2} MB, External: {3} MB",
            "Connection Established - Device Server Ws",
            "sendGatewayCommand method: doCommand command: version",
            @"Config loaded {{""env"": ""production"", ""debug"": false, ""workers"": {0}, ""port"": 3000}}",
            "Executing query: SELECT u.id, u.email, u.role FROM users u WHERE u.active = 1 ORDER BY u.created_at DESC LIMIT {0}"
        ]),
        ("info", [
            "HEARTBEAT: Service running - Uptime: {0}h {1}m | Process RSS: {2} MB | Heap Used: {3} MB / 32768 MB",
            "SmartUnit cache EXPIRED (age: {0}ms) - refreshing cache",
            "SmartUnit cache refresh: Found {0} SmartUnits to load",
            "Syncing endpoint devices",
            "Worker system is enabled. Processing user events.",
            "Scheduled task 'heartbeat' started (interval: 300000ms)",
            "Processing Engine is Running",
            "Alert Engine is Running",
            @"API Request: POST /api/v1/checkin {{""userId"": {0}, ""positionId"": {1}, ""action"": ""checkin""}}",
            @"API Response: 200 OK {{""success"": true, ""checkInId"": {0}, ""timestamp"": ""{1}""}}",
            @"User login successful {{""userId"": {0}, ""email"": ""user{1}@example.com"", ""role"": ""admin"", ""mfa"": true}}",
            @"Order processed {{""orderId"": ""ORD-{0}{1}"", ""items"": {2}, ""total"": {3}.95, ""currency"": ""USD""}}",
            @"Cache update {{""key"": ""session:{0}"", ""ttl"": 3600, ""size"": {1}, ""compressed"": false}}",
            "Query completed: INSERT INTO audit_log (user_id, action, ip_addr) VALUES ({0}, 'login', '10.42.0.{1}')"
        ]),
        ("warn", [
            "Slow query detected: {0}ms for SmartUnit.findAll",
            "Memory usage approaching threshold: RSS {0} MB / 4096 MB ({1}%)",
            "WebSocket reconnection attempt #{0} for gateway",
            "Rate limit approaching: {0}/100 requests in current window",
            @"Deprecated API called: GET /api/v1/legacy/devices {{""caller"": ""{0}"", ""version"": ""1.0""}}",
            @"Rate limit approaching {{""endpoint"": ""/api/v1/query"", ""current"": {0}, ""limit"": 150, ""window"": ""60s""}}",
            "Slow query ({0}ms): SELECT COUNT(*) FROM device_events WHERE device_id IN (SELECT id FROM devices WHERE rack_id = {1})"
        ]),
        ("error", [
            "Failed to connect to database: ETIMEDOUT after {0}ms",
            "Unhandled exception in processing engine: NullReferenceError",
            @"API Error: 500 {{""error"": ""Internal Server Error"", ""path"": ""/api/v1/positions/{0}"", ""code"": ""ERR_{1}""}}",
            "WebSocket connection lost to gateway at 10.42.0.{0}:{1}",
            "Cache corruption detected for rack {0} - forcing full rebuild",
            @"Validation failed {{""field"": ""email"", ""value"": null, ""rule"": ""required"", ""code"": ""ERR_VALIDATION_{0}""}}",
            @"Request failed {{""method"": ""POST"", ""url"": ""/api/v1/users/{0}"", ""status"": 500, ""duration"": {1}, ""retries"": {2}}}"
        ]),
        ("error", [
            "System.NullReferenceException: Object reference not set to an instance of an object."
        ]),
        ("error", [
            "   at NovaLog.Services.ProcessingEngine.Execute(WorkItem item) in D:\\src\\ProcessingEngine.cs:{0}"
        ]),
        ("error", [
            "   at NovaLog.Core.TaskRunner.RunAsync(CancellationToken ct) in D:\\src\\TaskRunner.cs:{0}"
        ]),
        ("fatal", [
            "Unhandled System.InvalidOperationException: Connection pool exhausted after {0}ms"
        ]),
        ("fatal", [
            "   at Database.ConnectionPool.Acquire(TimeSpan timeout) in D:\\src\\Database\\ConnectionPool.cs:{0}"
        ])
    ];

    public LogSimulator(string directory)
    {
        _directory = directory;
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
    }

    public bool IsRunning => _timer != null;

    /// <summary>
    /// Starts generating log lines at the given interval.
    /// </summary>
    public void Start(int intervalMs = 200)
    {
        if (_timer != null) return;

        _currentHour = DateTime.Now;
        _currentFile = GetLogFileName(_currentHour);

        // Write a seed line synchronously so the file exists before the audit JSON
        // references it — fixes the race where LoadLogsDirectory finds zero files.
        EmitLine(null);
        WriteAuditJson();

        _timer = new System.Threading.Timer(EmitLine, null, intervalMs, intervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void EmitLine(object? state)
    {
        if (_disposed) return;

        var now = DateTime.Now;

        // Rotate hourly (or if more than 1 minute has passed for testing)
        if (now.Hour != _currentHour.Hour || now.Minute != _currentHour.Minute)
        {
            _currentHour = now;
            _currentFile = GetLogFileName(now);
            if (!File.Exists(_currentFile))
                File.WriteAllText(_currentFile, "");
            WriteAuditJson();
        }

        try
        {
            var block = ShowcaseMode ? EmitShowcaseLine(now) : EmitStandardLine(now);
            File.AppendAllText(_currentFile!, block);
            _lineCount++;
        }
        catch (IOException) { /* file contention, skip */ }
    }

    private string EmitStandardLine(DateTime now)
    {
        var nl = Environment.NewLine;
        // Pick a weighted random level
        int roll = _rng.Next(100);
        int levelIdx = roll switch
        {
            < 60 => 0,  // debug
            < 85 => 1,  // info
            < 95 => 2,  // warn
            _    => 3   // error
        };

        var (level, messages) = Templates[levelIdx];
        var template = messages[_rng.Next(messages.Length)];
        var msg = string.Format(template,
            _rng.Next(1, 500), _rng.Next(1, 300), _rng.Next(100, 800), _rng.Next(1, 100));

        var ts = now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        // Occasionally emit a multi-line JSON block (1 in 12 lines)
        if (_rng.Next(12) == 0)
            return GenerateMultiLineJsonBlock(ts, level);
        return $"{ts} {level}: \t{msg}{nl}";
    }

    private string EmitShowcaseLine(DateTime now)
    {
        var nl = Environment.NewLine;
        var ts = now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        // Cycle through syntax types with weighted variety
        int roll = _rng.Next(100);
        return roll switch
        {
            < 15 => GenerateShowcaseJson(ts, nl),
            < 30 => GenerateShowcaseMultilineJson(ts, nl),
            < 42 => GenerateShowcaseSql(ts, nl),
            < 52 => GenerateShowcaseMultilineSql(ts, nl),
            < 62 => GenerateShowcaseStackTrace(ts, nl),
            < 72 => GenerateShowcaseNumbers(ts, nl),
            _ => GenerateShowcasePlain(ts, nl)
        };
    }

    private string GenerateShowcaseJson(string ts, string nl)
    {
        int v = _rng.Next(5);
        return v switch
        {
            0 => $"{ts} info: \tAPI Response: 200 OK {{\"success\": true, \"userId\": {_rng.Next(1, 999)}, \"token\": \"eyJhbGciOi.{_rng.Next(100000, 999999)}\"}}{nl}",
            1 => $"{ts} debug: \tCache entry {{\"key\": \"session:{_rng.Next(1000, 9999)}\", \"ttl\": 3600, \"size\": {_rng.Next(100, 5000)}, \"compressed\": false}}{nl}",
            2 => $"{ts} warn: \tValidation failed {{\"field\": \"email\", \"value\": null, \"rule\": \"required\", \"code\": \"ERR_{_rng.Next(1000, 9999)}\"}}{nl}",
            3 => $"{ts} info: \tOrder processed {{\"orderId\": \"ORD-{_rng.Next(10000, 99999)}\", \"items\": [{_rng.Next(1, 20)}], \"total\": {_rng.Next(10, 999)}.{_rng.Next(10, 99)}, \"currency\": \"USD\"}}{nl}",
            _ => $"{ts} debug: \tConfig loaded {{\"env\": \"production\", \"debug\": false, \"workers\": {_rng.Next(1, 16)}, \"port\": 3000, \"version\": \"2.{_rng.Next(0, 9)}.{_rng.Next(0, 50)}\"}}{nl}"
        };
    }

    private string GenerateShowcaseMultilineJson(string ts, string nl)
    {
        int v = _rng.Next(4);
        return v switch
        {
            0 => $"{ts} debug: \tprocessCommand {{{nl}" +
                 $"          executeMethod: 'doCommand',{nl}" +
                 $"          smartCommand: 'version',{nl}" +
                 $"          commandParams: {{ broadcast: true }},{nl}" +
                 $"          macAddress: '{_rng.Next(0xFFFF):x4}{_rng.Next(0xFFFF):x4}'{nl}" +
                 $"        }}{nl}",

            1 => $"{ts} info: \tdeviceEvent {{{nl}" +
                 $"          type: 'statusChange',{nl}" +
                 $"          deviceId: 'dev-{_rng.Next(1000, 9999)}',{nl}" +
                 $"          payload: {{{nl}" +
                 $"            online: true,{nl}" +
                 $"            rssi: -{_rng.Next(30, 90)},{nl}" +
                 $"            firmware: '2.{_rng.Next(0, 9)}.{_rng.Next(0, 20)}'{nl}" +
                 $"          }},{nl}" +
                 $"          timestamp: {DateTimeOffset.Now.ToUnixTimeMilliseconds()}{nl}" +
                 $"        }}{nl}",

            2 => $"{ts} debug: \tapiResponse {{{nl}" +
                 $"          status: 200,{nl}" +
                 $"          headers: {{ 'content-type': 'application/json', 'x-request-id': '{Guid.NewGuid():N}' }},{nl}" +
                 $"          body: {{{nl}" +
                 $"            success: true,{nl}" +
                 $"            data: [{nl}" +
                 $"              {{ id: {_rng.Next(1, 500)}, name: 'item-alpha', active: true }},{nl}" +
                 $"              {{ id: {_rng.Next(501, 999)}, name: 'item-beta', active: false }}{nl}" +
                 $"            ],{nl}" +
                 $"            total: 2{nl}" +
                 $"          }}{nl}" +
                 $"        }}{nl}",

            _ => $"{ts} info: \tuserProfile {{{nl}" +
                 $"          \"id\": {_rng.Next(1, 10000)},{nl}" +
                 $"          \"name\": \"User {_rng.Next(1, 500)}\",{nl}" +
                 $"          \"email\": \"user{_rng.Next(1, 500)}@example.com\",{nl}" +
                 $"          \"roles\": [\"admin\", \"editor\"],{nl}" +
                 $"          \"settings\": {{{nl}" +
                 $"            \"theme\": \"dark\",{nl}" +
                 $"            \"notifications\": true,{nl}" +
                 $"            \"timezone\": \"UTC-5\"{nl}" +
                 $"          }},{nl}" +
                 $"          \"lastLogin\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\"{nl}" +
                 $"        }}{nl}"
        };
    }

    private string GenerateShowcaseSql(string ts, string nl)
    {
        int v = _rng.Next(5);
        return v switch
        {
            0 => $"{ts} debug: \tExecuting: SELECT u.id, u.name, u.email FROM users u WHERE u.active = 1 AND u.role IN ('admin', 'editor') ORDER BY u.created_at DESC LIMIT {_rng.Next(10, 100)}{nl}",
            1 => $"{ts} debug: \tQuery: INSERT INTO audit_log (user_id, action, ip_addr, timestamp) VALUES ({_rng.Next(1, 500)}, 'login', '10.42.0.{_rng.Next(1, 254)}', NOW()){nl}",
            2 => $"{ts} debug: \tExecuting: UPDATE devices SET status = 'online', last_seen = NOW(), firmware_version = '2.{_rng.Next(0, 9)}.{_rng.Next(0, 20)}' WHERE device_id = {_rng.Next(1000, 9999)} AND rack_id = {_rng.Next(1, 50)}{nl}",
            3 => $"{ts} debug: \tQuery: SELECT d.id, d.name, COUNT(e.id) AS event_count FROM devices d LEFT JOIN device_events e ON d.id = e.device_id WHERE d.rack_id = {_rng.Next(1, 50)} GROUP BY d.id HAVING COUNT(e.id) > {_rng.Next(5, 100)} ORDER BY event_count DESC{nl}",
            _ => $"{ts} debug: \tExecuting: DELETE FROM sessions WHERE expires_at < NOW() AND user_id NOT IN (SELECT id FROM users WHERE role = 'admin'){nl}"
        };
    }

    private string GenerateShowcaseMultilineSql(string ts, string nl)
    {
        int v = _rng.Next(2);
        return v switch
        {
            0 => $"{ts} debug: \tExecuting query:{nl}" +
                 $"  SELECT u.id, u.name, u.email, r.name AS role_name,{nl}" +
                 $"         COUNT(o.id) AS order_count, SUM(o.total) AS total_spent{nl}" +
                 $"  FROM users u{nl}" +
                 $"  INNER JOIN roles r ON u.role_id = r.id{nl}" +
                 $"  LEFT JOIN orders o ON u.id = o.user_id{nl}" +
                 $"  WHERE u.active = 1{nl}" +
                 $"    AND u.created_at > '2025-01-01'{nl}" +
                 $"    AND r.name IN ('admin', 'manager'){nl}" +
                 $"  GROUP BY u.id, u.name, u.email, r.name{nl}" +
                 $"  HAVING COUNT(o.id) >= {_rng.Next(1, 20)}{nl}" +
                 $"  ORDER BY total_spent DESC{nl}" +
                 $"  LIMIT {_rng.Next(10, 100)}{nl}",

            _ => $"{ts} warn: \tSlow query ({_rng.Next(500, 5000)}ms):{nl}" +
                 $"  SELECT `item_id` AS `itemId`, `process_step_id` AS `processStepId`,{nl}" +
                 $"         `start`, `end`, `duration`, `created_at` AS `createdAt`{nl}" +
                 $"  FROM `itemhistory` AS `ItemHistory`{nl}" +
                 $"  WHERE (`ItemHistory`.`deleted_at` IS NULL{nl}" +
                 $"    AND (`ItemHistory`.`item_id` IN ({_rng.Next(1000, 2000)}, {_rng.Next(2000, 3000)}, {_rng.Next(3000, 4000)}){nl}" +
                 $"    AND `ItemHistory`.`process_step_id` = {_rng.Next(1, 20)})){nl}" +
                 $"  ORDER BY `ItemHistory`.`createdAt` DESC{nl}"
        };
    }

    private string GenerateShowcaseStackTrace(string ts, string nl)
    {
        int v = _rng.Next(3);
        return v switch
        {
            0 => $"{ts} error: \tSystem.NullReferenceException: Object reference not set to an instance of an object.{nl}" +
                 $"   at NovaLog.Services.ProcessingEngine.Execute(WorkItem item) in D:\\src\\Services\\ProcessingEngine.cs:line {_rng.Next(20, 200)}{nl}" +
                 $"   at NovaLog.Core.TaskRunner.RunAsync(CancellationToken ct) in D:\\src\\Core\\TaskRunner.cs:line {_rng.Next(50, 150)}{nl}" +
                 $"   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task){nl}",

            1 => $"{ts} error: \tSystem.InvalidOperationException: Connection pool exhausted after {_rng.Next(5000, 30000)}ms{nl}" +
                 $"   at Database.ConnectionPool.Acquire(TimeSpan timeout) in D:\\src\\Database\\ConnectionPool.cs:line {_rng.Next(80, 120)}{nl}" +
                 $"   at Database.QueryExecutor.ExecuteAsync(String sql, Object[] parameters) in D:\\src\\Database\\QueryExecutor.cs:line {_rng.Next(30, 90)}{nl}" +
                 $"   at Services.UserRepository.FindByEmail(String email) in D:\\src\\Services\\UserRepository.cs:line {_rng.Next(40, 100)}{nl}" +
                 $"   at Controllers.AuthController.Login(LoginRequest req) in D:\\src\\Controllers\\AuthController.cs:line {_rng.Next(20, 60)}{nl}",

            _ => $"{ts} fatal: \tSystem.IO.FileNotFoundException: Could not find file 'D:\\data\\config\\app-settings.json'.{nl}" +
                 $"   at System.IO.FileStream..ctor(String path, FileMode mode, FileAccess access) in D:\\src\\System.IO\\FileStream.cs:line {_rng.Next(100, 300)}{nl}" +
                 $"   at NovaLog.Configuration.SettingsLoader.LoadFromDisk(String path) in D:\\src\\Configuration\\SettingsLoader.cs:line {_rng.Next(20, 80)}{nl}" +
                 $"   at NovaLog.Program.Main(String[] args) in D:\\src\\Program.cs:line {_rng.Next(10, 30)}{nl}"
        };
    }

    private string GenerateShowcaseNumbers(string ts, string nl)
    {
        int v = _rng.Next(5);
        return v switch
        {
            0 => $"{ts} info: \tHEARTBEAT: Uptime: {_rng.Next(1, 48)}h {_rng.Next(0, 59)}m {_rng.Next(0, 59)}s | RSS: {_rng.Next(100, 512)} MB | Heap: {_rng.Next(50, 300)} MB / 32768 MB | GC: {_rng.Next(0, 50)} collections{nl}",
            1 => $"{ts} debug: \tRequest completed in {_rng.Next(1, 2000)}ms | payload: {_rng.Next(100, 50000)} bytes | status: 200 | cache: HIT{nl}",
            2 => $"{ts} warn: \tMemory grew by {_rng.Next(10, 200)} MB in the last 60s (from {_rng.Next(100, 300)} MB to {_rng.Next(300, 500)} MB) - threshold: 4096 MB{nl}",
            3 => $"{ts} info: \tBatch processed: {_rng.Next(100, 5000)} items in {_rng.Next(500, 10000)}ms ({_rng.Next(1, 99)}.{_rng.Next(10, 99)} items/sec) | errors: 0 | retries: {_rng.Next(0, 5)}{nl}",
            _ => $"{ts} debug: \tDevice signal: RSSI -{_rng.Next(30, 90)}dBm | SNR {_rng.Next(5, 30)}.{_rng.Next(0, 9)}dB | channel {_rng.Next(1, 13)} | freq 2.4{_rng.Next(12, 84)}GHz | TX power {_rng.Next(10, 23)}dBm{nl}"
        };
    }

    private string GenerateShowcasePlain(string ts, string nl)
    {
        int v = _rng.Next(6);
        return v switch
        {
            0 => $"{ts} debug: \tEntering checkForIdleWorkers{nl}",
            1 => $"{ts} info: \tProcessing Engine is Running{nl}",
            2 => $"{ts} debug: \tConnection Established - Device Server Ws{nl}",
            3 => $"{ts} info: \tSyncing endpoint devices{nl}",
            4 => $"{ts} debug: \tWorker system is enabled. Processing user events.{nl}",
            _ => $"{ts} info: \tScheduled task 'cleanup' completed successfully{nl}"
        };
    }

    private string GenerateMultiLineJsonBlock(string ts, string level)
    {
        var nl = Environment.NewLine;
        int variant = _rng.Next(3);
        return variant switch
        {
            0 => $"{ts} {level}: \tprocessCommand {{{nl}" +
                 $"          executeMethod: 'doCommand',{nl}" +
                 $"          smartCommand: 'version',{nl}" +
                 $"          commandParams: {{ broadcast: true }},{nl}" +
                 $"          macAddress: '{_rng.Next(0xFFFF):x4}{_rng.Next(0xFFFF):x4}'{nl}" +
                 $"        }}{nl}",

            1 => $"{ts} {level}: \tdeviceEvent {{{nl}" +
                 $"          type: 'statusChange',{nl}" +
                 $"          deviceId: 'dev-{_rng.Next(1000, 9999)}',{nl}" +
                 $"          payload: {{{nl}" +
                 $"            online: true,{nl}" +
                 $"            rssi: -{_rng.Next(30, 90)},{nl}" +
                 $"            firmware: '2.{_rng.Next(0, 9)}.{_rng.Next(0, 20)}'{nl}" +
                 $"          }},{nl}" +
                 $"          timestamp: {DateTimeOffset.Now.ToUnixTimeMilliseconds()}{nl}" +
                 $"        }}{nl}",

            _ => $"{ts} {level}: \tapiResponse {{{nl}" +
                 $"          status: 200,{nl}" +
                 $"          headers: {{ 'content-type': 'application/json', 'x-request-id': '{Guid.NewGuid():N}' }},{nl}" +
                 $"          body: {{{nl}" +
                 $"            success: true,{nl}" +
                 $"            data: [{nl}" +
                 $"              {{ id: {_rng.Next(1, 500)}, name: 'item-alpha' }},{nl}" +
                 $"              {{ id: {_rng.Next(501, 999)}, name: 'item-beta' }}{nl}" +
                 $"            ],{nl}" +
                 $"            total: 2{nl}" +
                 $"          }}{nl}" +
                 $"        }}{nl}"
        };
    }

    private string GetLogFileName(DateTime dt) =>
        Path.Combine(_directory, $"sf-{dt:yyyy-MM-dd-HH}.log");

    private void WriteAuditJson()
    {
        // Build files array from all existing sf-*.log files in the directory
        var logFiles = Directory.GetFiles(_directory, "sf-????-??-??-??.log")
            .OrderBy(f => f)
            .ToList();

        var filesArray = logFiles.Select(f => new
        {
            date = new DateTimeOffset(File.GetCreationTime(f)).ToUnixTimeMilliseconds(),
            name = f,
            hash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(f)))
        }).ToList();

        var audit = new
        {
            keep = new { days = true, amount = 30 },
            auditLog = Path.Combine(_directory, "sf-audit.json"),
            files = filesArray,
            hashType = "sha256"
        };

        var json = JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true });
        var auditPath = Path.Combine(_directory, "sf-audit.json");

        // Write atomically via temp file — catch contention from readers
        try
        {
            var tmp = auditPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, auditPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File locked by a reader — skip this audit update; next rotation will retry.
        }
    }

    public IVirtualLogProvider GetProvider()
    {
        if (string.IsNullOrEmpty(_currentFile))
        {
            _currentHour = DateTime.Now;
            _currentFile = GetLogFileName(_currentHour);
            if (!File.Exists(_currentFile)) File.WriteAllText(_currentFile, "");
        }
        return new BigFileLogProvider(_currentFile);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

namespace SISI;

public class HealthConf
{
    public bool enable { get; set; }

    /// <summary>số giờ giữa hai lần kiểm</summary>
    public int interval { get; set; }

    /// <summary>timeout mỗi request, giây</summary>
    public int timeout { get; set; }

    /// <summary>số request song song</summary>
    public int parallel { get; set; }

    /// <summary>danh sách route muốn kiểm (vd "phub", "xds"); rỗng = mọi route có module trong module/Adult</summary>
    public string[] modules { get; set; }

    /// <summary>route bỏ qua</summary>
    public string[] exclude { get; set; }
}

public class HealthItem
{
    public string name { get; set; }
    public string path { get; set; }

    /// <summary>ok | empty | timeout | na | error</summary>
    public string status { get; set; }

    public int items { get; set; }

    /// <summary>total_pages của trang 1</summary>
    public int total { get; set; }
    public int code { get; set; }
    public long ms { get; set; }
    public string error { get; set; }
    public DateTime updated { get; set; }
}

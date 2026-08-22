using System.Collections.Generic;

namespace KKPhim;

public sealed class KkSearchResponse
{
    public KkSearchData data { get; set; }
}

public sealed class KkSearchData
{
    public List<KkMovie> items { get; set; }
    public KkSearchParams @params { get; set; }
}

public sealed class KkSearchParams
{
    public KkPagination pagination { get; set; }
}

public sealed class KkPagination
{
    public int totalItems { get; set; }
    public int totalItemsPerPage { get; set; }
    public int currentPage { get; set; }
    public int totalPages { get; set; }
}

public sealed class KkDetailResponse
{
    public KkMovie movie { get; set; }
    public List<KkEpisodeServer> episodes { get; set; }
}

public sealed class KkMovie
{
    public KkExternalId tmdb { get; set; }
    public KkExternalId imdb { get; set; }
    public string _id { get; set; }
    public string name { get; set; }
    public string slug { get; set; }
    public string origin_name { get; set; }
    public string content { get; set; }
    public string type { get; set; }
    public string status { get; set; }
    public string thumb_url { get; set; }
    public string poster_url { get; set; }
    public string time { get; set; }
    public string episode_current { get; set; }
    public int episode_total { get; set; }
    public string quality { get; set; }
    public string lang { get; set; }
    public string[] lang_key { get; set; }
    public int year { get; set; }
    public List<KkNamedItem> category { get; set; }
    public List<KkNamedItem> country { get; set; }
}

public sealed class KkExternalId
{
    public string id { get; set; }
    public string type { get; set; }
    public int? season { get; set; }
    public double vote_average { get; set; }
    public int vote_count { get; set; }
}

public sealed class KkNamedItem
{
    public string name { get; set; }
    public string slug { get; set; }
    public string id { get; set; }
}

public sealed class KkEpisodeServer
{
    public string server_name { get; set; }
    public bool is_ai { get; set; }
    public List<KkEpisode> server_data { get; set; }
}

public sealed class KkEpisode
{
    public string name { get; set; }
    public string slug { get; set; }
    public string filename { get; set; }
    public string link_embed { get; set; }
    public string link_m3u8 { get; set; }
    public int season_number { get; set; }
    public int episode_number { get; set; }
    public int season { get; set; }
    public int episode { get; set; }
}

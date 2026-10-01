namespace Medios.DTO
{
    public class PagedResult<T>
    {
        public int Total { get; set; }
        public int Pagina { get; set; }
        public int TamañoPagina { get; set; }
        public List<T> Data { get; set; } = new();
    }
}

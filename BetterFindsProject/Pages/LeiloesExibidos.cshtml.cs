using System.Data.SqlClient;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetterFindsProject.Pages
{
    public class LeiloesExibidosModel : PageModel
    {
        private readonly string _connectionString;

        // Usamos este construtor para obter a string conection que permite a conex�o com a base de dados
        public LeiloesExibidosModel(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Lida com os dados enviados pelo formul�rio HTLM ([BindProperty]serve para fazer essa liga��o autom�ticamente) 
        public class AuctionItem
        {
            public int AuctionId { get; set; }
            public string Name { get; set; }
            public int Price { get; set; }
            public int AuctionTime { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string Description { get; set; }
            public int MinimumBid { get; set; }
            public int ClientId { get; set; }
            public bool IsAuctionEnded { get; set; }
            public bool IsWinner { get; set; }
            public bool IsCreator { get; set; }

        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            public int Licitacao { get; set; }
        }

        public List<AuctionItem> Auctions { get; set; } = new List<AuctionItem>();

        public async Task OnGetAsync()
        {
            // Vai buscar os leil�es � base de dados
            Auctions = await GetAuctionsFromDatabaseAsync();

            // Itera sobre os leil�es 
            foreach (var auction in Auctions)
            {
                // Verifica se os leil�es terminararem comprarando o tempo atual com o tempo de fim do leil�o
                auction.IsAuctionEnded = DateTime.Now > auction.EndTime;

                // Verifica se o cliente logado � o criador do leil�o
                auction.IsCreator = await IsCreator(auction.AuctionId);

                if (auction.IsAuctionEnded)
                {
                    // Verifica se o cliente logado � o vencedor do leil�o se o leil�o terminou
                    auction.IsWinner = await IsWinner(auction.AuctionId);
                }
            }
        }

        /* Processa os dados do formul�rio enviado pelo usutilizador, valida os dados, e atualiza a licita��o do leil�o 
        * na base de dados, se todas as valida��es forem realizadas com sucesso */
        public async Task<IActionResult> OnPostAsync(int auctionId)
        {
            try
            {
                if (!User.Identity.IsAuthenticated)
                {
                    return RedirectToPage("/Login");
                }

                string userEmail = User.Identity.Name;
                string queryGetClientId = "SELECT ClientId FROM Client WHERE Email = @UserEmail";

                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    await con.OpenAsync();

                    // Obter o ClientId do utilizador autenticado
                    int clientId = await con.ExecuteScalarAsync<int>(queryGetClientId, new { UserEmail = userEmail });

                    // Verifica se o leil�o est� encerrado
                    var auctionInfo = await con.QueryFirstOrDefaultAsync<AuctionItem>("SELECT * FROM Auction WHERE AuctionId = @AuctionId", new { AuctionId = auctionId });
                    if (auctionInfo.IsAuctionEnded)
                    {
                        ModelState.AddModelError(string.Empty, "Este leil�o j� terminou.");
                        return RedirectToPage("/Leiloes");
                    }

                    // N�o permite fazer licita��o inferior � MinimumBid
                    if (Input.Licitacao < auctionInfo.MinimumBid)
                    {
                        ModelState.AddModelError(string.Empty, "A sua licita��o � inferior � licita��o m�nima estabelecida pelo vendedor.");
                        return RedirectToPage("/Leiloes");
                    }

                    // N�o permite fazer licita��o inferior ao pre�o atual
                    if (Input.Licitacao < auctionInfo.Price)
                    {
                        ModelState.AddModelError(string.Empty, "No momento existe uma licita��o maior");
                        return RedirectToPage("/Leiloes");
                    }

                    // Atualiza o pre�o na tabela de leil�es
                    string queryUpdateAuction = "UPDATE Auction SET Licitacao = @Licitacao, Price = @Licitacao WHERE AuctionId = @AuctionId";
                    using (SqlCommand cmdUpdateAuction = new SqlCommand(queryUpdateAuction, con))
                    {
                        cmdUpdateAuction.Parameters.AddWithValue("@Licitacao", Input.Licitacao);
                        cmdUpdateAuction.Parameters.AddWithValue("@AuctionId", auctionId);

                        int rowsAffectedAuction = await cmdUpdateAuction.ExecuteNonQueryAsync();

                        if (rowsAffectedAuction <= 0)
                        {
                            ModelState.AddModelError(string.Empty, "Falha ao atualizar o leil�o. Nenhuma linha afetada.");
                            return Page();
                        }
                    }

                    // Insere o novo lance na tabela da Bid
                    string queryInsertBid = "INSERT INTO Bid (Value, Status, Time, ClientId, AuctionId) VALUES (@Value, @Status, @Time, @ClientId, @AuctionId)";
                    using (SqlCommand cmdInsertBid = new SqlCommand(queryInsertBid, con))
                    {
                        cmdInsertBid.Parameters.AddWithValue("@Value", Input.Licitacao);
                        cmdInsertBid.Parameters.AddWithValue("@Status", "A sua descri��o de status aqui"); // Substitua pela descri��o correta
                        cmdInsertBid.Parameters.AddWithValue("@Time", DateTime.Now);
                        cmdInsertBid.Parameters.AddWithValue("@ClientId", clientId);
                        cmdInsertBid.Parameters.AddWithValue("@AuctionId", auctionId);

                        int rowsAffectedBid = await cmdInsertBid.ExecuteNonQueryAsync();

                        if (rowsAffectedBid <= 0)
                        {
                            ModelState.AddModelError(string.Empty, "Falha ao criar a oferta. Nenhuma linha afetada na tabela de lances.");
                            return Page();
                        }
                    }

                    return RedirectToPage("/Leiloes");
                }
            }
            catch (SqlException sqlEx)
            {
                ModelState.AddModelError(string.Empty, $"Erro SQL: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Ocorreu um erro: {ex.Message}");
            }
            return Page();
        }

        // Esta fun��o vai buscar todos os leil�es � base de dados
        private async Task<List<AuctionItem>> GetAuctionsFromDatabaseAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var auctions = await connection.QueryAsync<AuctionItem>("SELECT * FROM Auction");
                return auctions.ToList();
            }
        }

        private async Task<bool> IsWinner(int auctionId)
        {
            // Vai buscar os Id�s do vencedor do leil�o e do cliente logado
            int? highestBidderClientId = await GetHighestBidderClientId(auctionId);
            int? loggedInClientId = await GetLoggedInClientId();

            // Verifica se o vencedor do leil�o � o cliente que est� logado
            return highestBidderClientId.HasValue && highestBidderClientId.Value == loggedInClientId;
        }

        private async Task<bool> IsCreator(int auctionId)
        {
            // Vai buscar os Id�s do criador do leil�o e do cliente logado
            int? auctionCreatorClientId = await GetAuctionCreatorClientId(auctionId);
            int? loggedInClientId = await GetLoggedInClientId();

            // Verifica se o criador do leil�o � o cliente que est� logado
            return auctionCreatorClientId.HasValue && auctionCreatorClientId.Value == loggedInClientId;
        }

        // Consulta para obter o ClientId do maior licitante do leil�o
        private async Task<int?> GetHighestBidderClientId(int auctionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string query = "SELECT TOP 1 ClientId FROM Bid WHERE AuctionId = @AuctionId ORDER BY Value DESC";

                return await connection.ExecuteScalarAsync<int?>(query, new { AuctionId = auctionId });
            }
        }

        // Consulta para obter o ClientId do criador do leil�o
        private async Task<int?> GetAuctionCreatorClientId(int auctionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Consulta para obter o ClientId do criador do leil�o
                string query = "SELECT ClientId FROM Auction WHERE AuctionId = @AuctionId";

                return await connection.ExecuteScalarAsync<int?>(query, new { AuctionId = auctionId });
            }
        }

        // Consulta para obter o ClientId do cliente logado
        private async Task<int?> GetLoggedInClientId()
        {
            if (User.Identity.IsAuthenticated)
            {
                string userEmail = User.Identity.Name;
                string queryGetClientId = "SELECT ClientId FROM Client WHERE Email = @UserEmail";

                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    await con.OpenAsync();
                    return await con.ExecuteScalarAsync<int?>(queryGetClientId, new { UserEmail = userEmail });
                }
            }

            return null;
        }
    }
}


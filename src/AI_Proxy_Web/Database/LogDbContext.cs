using Microsoft.EntityFrameworkCore;

namespace AI_Proxy_Web.Database;

public class LogDbContext:DbContext
    {
        public LogDbContext(DbContextOptions<LogDbContext> options) : base(options)
        {
        }
        
        public DbSet<ChatGptLog> ChatGptLogs { get; set; }
        public DbSet<ChatGptFunction> ChatGptFunctions { get; set; }
        public DbSet<ChatGptPrompt> ChatGptPrompts { get; set; }
        public DbSet<FeiShuUserAccessToken> FeiShuUserAccessTokens { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ChatGptLog>(entity => { entity.HasKey(e => e.Id); });
            modelBuilder.Entity<ChatGptFunction>(entity => { entity.HasKey(e => e.Id); });
            modelBuilder.Entity<ChatGptPrompt>(entity => { entity.HasKey(e => e.Id); });
            modelBuilder.Entity<FeiShuUserAccessToken>(entity =>
            {
                entity.HasKey(e => new { e.app_id, e.user_id });
            });
            
        }
    }
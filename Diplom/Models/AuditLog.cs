using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Diplom.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "Пользователь")]
        [MaxLength(450)]
        public string? UserId { get; set; }

        [Display(Name = "Имя пользователя")]
        [MaxLength(256)]
        public string? UserName { get; set; }

        [Display(Name = "Действие")]
        [MaxLength(100)]
        public string? Action { get; set; }

        [Display(Name = "Сущность")]
        [MaxLength(100)]
        public string? Entity { get; set; }

        [Display(Name = "ID сущности")]
        public int? EntityId { get; set; }

        [Display(Name = "Детали")]
        [MaxLength(2000)]
        public string? Details { get; set; }

        [Display(Name = "IP адрес")]
        [MaxLength(50)]
        public string? IpAddress { get; set; }

        [Display(Name = "Дата и время")]
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
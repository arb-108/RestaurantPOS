using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Domain.Entities;

public class Employee : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? CNIC { get; set; }
    public string? Address { get; set; }
    public string? EmergencyContact { get; set; }

    // Job info
    public EmployeeCategory Category { get; set; } = EmployeeCategory.Service;
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public string? Designation { get; set; }
    public DateTime JoiningDate { get; set; } = DateTime.UtcNow;
    public DateTime? LeavingDate { get; set; }

    // Salary
    public long BasicSalary { get; set; }       // paisa
    public long Allowances { get; set; }         // paisa
    public long Deductions { get; set; }         // paisa

    // Link to User (optional — not all employees have system login)
    public int? UserId { get; set; }
    public User? User { get; set; }

    // Navigation
    public ICollection<Payroll> Payrolls { get; set; } = [];
}

public class Payroll : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public int Month { get; set; }     // 1-12
    public int Year { get; set; }
    public long BasicSalary { get; set; }       // paisa
    public long Allowances { get; set; }         // paisa
    public long Deductions { get; set; }         // paisa
    public long Bonus { get; set; }              // paisa
    public long Advance { get; set; }            // paisa (salary advance deducted)
    public long NetSalary { get; set; }          // paisa (calculated)
    public PayrollStatus Status { get; set; } = PayrollStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public string? Notes { get; set; }

    // Link to expense when salary is paid
    public int? ExpenseId { get; set; }
}

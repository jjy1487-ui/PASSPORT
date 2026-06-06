using UnityEngine;

/// <summary>취업증빙(진실). employment_id, customer_id, cert_no, company_name, job_title, hire_date, issue_date
/// (EmploymentCard 양식: 만료일 제거, 입사일 추가. 표시 name 은 생성기가 여권 영문이름으로 조인)</summary>
[CreateAssetMenu(fileName = "EmploymentCertTable", menuName = "Passport/Data/Employment Cert Table")]
public class EmploymentCertTable : DataTableAsset { }

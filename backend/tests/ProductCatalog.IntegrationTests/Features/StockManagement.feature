Feature: Stock management
    As a warehouse operator
    I want to add and remove units of a product
    So that the catalogue always shows the stock that is really available

Background:
    Given a product "Polarised Aviator" with 10 units in stock

Scenario: Removing units that are available
    When I remove 3 units from stock
    Then the request succeeds
    And the product has 7 units in stock

Scenario: Removing more units than are available
    When I remove 11 units from stock
    Then the request is rejected as a conflict
    And the product has 10 units in stock

Scenario: Restocking a product
    When I add 15 units to stock
    Then the request succeeds
    And the product has 25 units in stock

Scenario Outline: Quantities must be positive
    When I add <quantity> units to stock
    Then the request is rejected as invalid
    And the product has 10 units in stock

    Examples:
      | quantity |
      | 0        |
      | -4       |
